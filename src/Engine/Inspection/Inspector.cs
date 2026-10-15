using System.Buffers;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Engine;

/// <summary>
/// Lets a development tool look inside the running game. When the tool starts the game it sets
/// <see cref="ENVIRONMENT_VARIABLE"/> to the path of a Unix domain socket it's listening on; the game connects
/// at startup and answers the tool's requests between frames with a tree built from every registered
/// <see cref="IInspectable"/>. Without the variable nothing happens, and the per-frame cost is one null check.
/// </summary>
/// <remarks>
/// The protocol is one JSON object per line, both ways. On connecting the game sends
/// <c>{"hello":"engine","protocol":1,"pid":…}</c>. The tool sends <c>{"id":7,"expanded":["world-0","world-0/12"]}</c>
/// and the game replies <c>{"id":7,"frame":…,"roots":[node…]}</c>, where a node is
/// <c>{"k":key,"l":label,"n":note,"v":value,"s":valueIsSummary,"x":expandable,"c":[children]}</c> with absent
/// fields omitted. Only the latest pending request is answered, once per frame at most.
/// </remarks>
public static class Inspector
{
    public const string ENVIRONMENT_VARIABLE = "ENGINE_INSPECTOR";

    public const int PROTOCOL_VERSION = 1;

    static readonly List< WeakReference< IInspectable > > Sources = new();

    static InspectorLink? Link;

    static long Frame;

    /// <summary>
    /// Adds a source to the tree. Held weakly, so a world that's dropped leaves the tree on its own. Call on
    /// the game thread.
    /// </summary>
    public static void Register ( IInspectable source )
    {
        Sources.RemoveAll( reference => !reference.TryGetTarget( out var target ) || ReferenceEquals( target, source ) );
        Sources.Add( new WeakReference< IInspectable >( source ) );
    }

    public static void Unregister ( IInspectable source ) =>
        Sources.RemoveAll( reference => !reference.TryGetTarget( out var target ) || ReferenceEquals( target, source ) );

    /// <summary>Every registered source's root row, as the tool asking with <paramref name="query"/> would see them.</summary>
    public static List< InspectorNode > Snapshot ( InspectorQuery query )
    {
        var roots = new List< InspectorNode >();
        Sources.RemoveAll( reference => !reference.TryGetTarget( out _ ) );
        foreach ( var reference in Sources )
        {
            if ( !reference.TryGetTarget( out var source ) )
                continue;

            try
            {
                roots.Add( source.Inspect( query ) );
            }
            catch ( Exception exception )
            {
                roots.Add( new InspectorNode( source.GetType().FullName ?? source.GetType().Name, source.GetType().Name )
                {
                    value = $"<{exception.GetType().Name}: {exception.Message}>",
                    valueIsSummary = true,
                } );
            }
        }

        return roots;
    }

    /// <summary>Connects to the tool named by <see cref="ENVIRONMENT_VARIABLE"/>, if there is one.</summary>
    internal static void Start ()
    {
        string? path = Environment.GetEnvironmentVariable( ENVIRONMENT_VARIABLE );
        if ( string.IsNullOrEmpty( path ) || Link != null || OperatingSystem.IsBrowser() )
            return;

        if ( Connect( path ) is { } error )
            Log.Warning( $"Inspector: couldn't connect to {path}: {error}" );
        else
            Log.Info( "Inspector connected." );
    }

    /// <summary>Connects to the tool listening at <paramref name="path"/>. Returns why it couldn't, or null.</summary>
    internal static string? Connect ( string path )
    {
        Link = InspectorLink.Connect( path, out string? error );
        return error;
    }

    /// <summary>Answers the tool's latest request, if it has one. Called once per frame, after the update.</summary>
    internal static void Pump ()
    {
        if ( Link == null )
            return;

        Frame++;
        if ( Link.isClosed )
        {
            Log.Info( "Inspector disconnected." );
            Stop();
            return;
        }

        if ( Link.TakeLatestRequest() is { } request )
            Link.Send( WriteResponse( request.id, Snapshot( new InspectorQuery( request.expanded ) ) ) );
    }

    internal static void Stop ()
    {
        Link?.Dispose();
        Link = null;
    }

    internal static byte[] WriteResponse ( long id, List< InspectorNode > roots )
    {
        var buffer = new ArrayBufferWriter< byte >();
        using ( var writer = new Utf8JsonWriter( buffer ) )
        {
            writer.WriteStartObject();
            writer.WriteNumber( "id", id );
            writer.WriteNumber( "frame", Frame );
            writer.WriteStartArray( "roots" );
            foreach ( var root in roots )
                WriteNode( writer, root );

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        buffer.Write( "\n"u8 );
        return buffer.WrittenSpan.ToArray();
    }

    static void WriteNode ( Utf8JsonWriter writer, InspectorNode node )
    {
        writer.WriteStartObject();
        writer.WriteString( "k", node.key );
        writer.WriteString( "l", node.label );
        if ( node.note != null )
            writer.WriteString( "n", node.note );

        if ( node.value != null )
            writer.WriteString( "v", node.value );

        if ( node.valueIsSummary )
            writer.WriteBoolean( "s", true );

        if ( node.expandable || node.children is { Count: > 0 } )
            writer.WriteBoolean( "x", true );

        if ( node.children != null )
        {
            writer.WriteStartArray( "c" );
            foreach ( var child in node.children )
                WriteNode( writer, child );

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    internal readonly record struct Request ( long id, string[] expanded );

    /// <summary>Reads one request line. Null for anything that isn't one, which is ignored rather than fatal.</summary>
    internal static Request? ParseRequest ( string line )
    {
        try
        {
            using var document = JsonDocument.Parse( line );
            var root = document.RootElement;
            if ( root.ValueKind != JsonValueKind.Object || !root.TryGetProperty( "id", out var id ) || !id.TryGetInt64( out long requestId ) )
                return null;

            var expanded = new List< string >();
            if ( root.TryGetProperty( "expanded", out var paths ) && paths.ValueKind == JsonValueKind.Array )
            {
                foreach ( var path in paths.EnumerateArray() )
                {
                    if ( path.ValueKind == JsonValueKind.String )
                        expanded.Add( path.GetString()! );
                }
            }

            return new Request( requestId, expanded.ToArray() );
        }
        catch ( JsonException )
        {
            return null;
        }
    }
}

/// <summary>
/// The connection to the tool. Requests are read on one background thread and replies written on another,
/// so a slow or stalled tool never holds up a frame: the game thread only ever swaps a request out and
/// queues a reply, and replies the tool isn't keeping up with are dropped.
/// </summary>
sealed class InspectorLink : IDisposable
{
    const int MAX_QUEUED_REPLIES = 2;

    readonly Socket socket;
    readonly NetworkStream stream;
    readonly BlockingCollection< byte[] > replies = new( MAX_QUEUED_REPLIES );
    Inspector.Request? latestRequest;
    readonly Lock requestLock = new();
    volatile bool closed;

    public bool isClosed => closed;

    InspectorLink ( Socket socket )
    {
        this.socket = socket;
        stream = new NetworkStream( socket, ownsSocket: false );
        new Thread( ReadRequests ) { IsBackground = true, Name = "Inspector reader" }.Start();
        new Thread( WriteReplies ) { IsBackground = true, Name = "Inspector writer" }.Start();
    }

    public static InspectorLink? Connect ( string path, out string? error )
    {
        var socket = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        try
        {
            socket.Connect( new UnixDomainSocketEndPoint( path ) );
        }
        catch ( Exception exception ) when ( exception is SocketException or ArgumentException or PlatformNotSupportedException )
        {
            error = exception.Message;
            socket.Dispose();
            return null;
        }

        error = null;
        var link = new InspectorLink( socket );
        link.Send( Encoding.UTF8.GetBytes(
            $"{{\"hello\":\"engine\",\"protocol\":{Inspector.PROTOCOL_VERSION},\"pid\":{Environment.ProcessId}}}\n" ) );
        return link;
    }

    public Inspector.Request? TakeLatestRequest ()
    {
        lock ( requestLock )
        {
            var request = latestRequest;
            latestRequest = null;
            return request;
        }
    }

    public void Send ( byte[] reply )
    {
        if ( closed )
            return;

        try
        {
            replies.TryAdd( reply );
        }
        catch ( InvalidOperationException )
        {
            // Disposed between the check and the add.
        }
    }

    void ReadRequests ()
    {
        try
        {
            using var reader = new StreamReader( stream, Encoding.UTF8 );
            while ( reader.ReadLine() is { } line )
            {
                if ( Inspector.ParseRequest( line ) is not { } request )
                    continue;

                lock ( requestLock )
                    latestRequest = request;
            }
        }
        catch ( Exception exception ) when ( exception is IOException or ObjectDisposedException or SocketException )
        {
            // The tool went away, or the link was disposed; either way the link is closed.
        }

        closed = true;
    }

    void WriteReplies ()
    {
        try
        {
            foreach ( var reply in replies.GetConsumingEnumerable() )
                stream.Write( reply );
        }
        catch ( Exception exception ) when ( exception is IOException or ObjectDisposedException or SocketException or InvalidOperationException )
        {
            // As above.
        }

        closed = true;
    }

    public void Dispose ()
    {
        closed = true;
        replies.CompleteAdding();
        try
        {
            socket.Shutdown( SocketShutdown.Both );
        }
        catch ( SocketException )
        {
            // Already disconnected.
        }

        stream.Dispose();
        socket.Dispose();
    }
}
