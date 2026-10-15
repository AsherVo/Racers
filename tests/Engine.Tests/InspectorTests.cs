using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Engine.Tests;

public class InspectorTests
{
    sealed class Car
    {
        public string? name;
        public int speed;
        public Vector2 position;
        public List< string > tags = new();
    }

    sealed class Garage : IInspectable
    {
        public InspectorNode Inspect ( InspectorQuery query ) =>
            InspectorNode.Describe( "garage", "Garage", new Car { name = "Red", speed = 3, tags = { "fast" } }, "garage", query );
    }

    [Fact]
    public void ACollapsedObjectIsSummarizedWithoutChildren ()
    {
        var node = InspectorNode.Describe( "car", "Car", new Car { name = "Red", speed = 3 }, "car", InspectorQuery.Nothing );

        Assert.True( node.expandable );
        Assert.True( node.valueIsSummary );
        Assert.Null( node.children );
        Assert.Equal( "{name: Red, speed: 3, position: {X: 0.0, Y: 0.0}, tags: []}", node.value );
    }

    [Fact]
    public void AnExpandedObjectHasAChildPerField ()
    {
        var query = new InspectorQuery( ["car", "car/position"] );
        var node = InspectorNode.Describe( "car", "Car", new Car { name = "Red", position = new Vector2( 1, 2.5f ) }, "car", query );

        Assert.Equal( ["name", "speed", "position", "tags"], node.children!.Select( child => child.key ) );
        Assert.Equal( "Red", node.children[0].value );
        Assert.False( node.children[0].expandable );

        var position = node.children[2];
        Assert.Equal( "{X: 1.0, Y: 2.5}", position.value );
        Assert.Equal( ["1.0", "2.5"], position.children!.Select( child => child.value ) );

        // Empty, so nothing to expand, and not asked for.
        Assert.False( node.children[3].expandable );
    }

    [Fact]
    public void ListItemsAreLabelledByIndex ()
    {
        var query = new InspectorQuery( ["car", "car/tags"] );
        var node = InspectorNode.Describe( "car", "Car", new Car { tags = { "fast", "red" } }, "car", query );
        var tags = node.children!.Single( child => child.key == "tags" );

        Assert.Equal( "[fast, red]", tags.value );
        Assert.Equal( ["[0]", "[1]"], tags.children!.Select( child => child.label ) );
        Assert.Equal( ["0", "1"], tags.children!.Select( child => child.key ) );
    }

    [Fact]
    public void ALongSummaryFallsBackToACount ()
    {
        var car = new Car { name = new string( 'x', 100 ) };
        var node = InspectorNode.Describe( "car", "Car", car, "car", InspectorQuery.Nothing );

        Assert.Equal( "4 fields", node.value );
    }

    [Fact]
    public void NullIsAValue ()
    {
        var node = InspectorNode.Describe( "car", "Car", null, "car", InspectorQuery.Nothing );

        Assert.Equal( "null", node.value );
        Assert.False( node.expandable );
    }

    [Fact]
    public void KeysAreEscapedInPaths ()
    {
        Assert.Equal( "a/b%2Fc", InspectorQuery.Join( "a", "b/c" ) );
        Assert.Equal( "a/100%25", InspectorQuery.Join( "a", "100%" ) );
        Assert.Equal( "root", InspectorQuery.Join( "", "root" ) );
    }

    [Fact]
    public void RequestsAreReadAndAnythingElseIsIgnored ()
    {
        var request = Inspector.ParseRequest( """{"id":7,"expanded":["world-0","world-0/12"]}""" );

        Assert.NotNull( request );
        Assert.Equal( 7, request.Value.id );
        Assert.Equal( ["world-0", "world-0/12"], request.Value.expanded );
        Assert.Null( Inspector.ParseRequest( "not json" ) );
        Assert.Null( Inspector.ParseRequest( """{"expanded":[]}""" ) );
        Assert.Null( Inspector.ParseRequest( "[1, 2]" ) );
    }

    [Fact]
    public void ResponsesAreOneLineOfJsonWithShortKeys ()
    {
        var root = new InspectorNode( "world-0", "World 0" ) { value = "1 entity", valueIsSummary = true, expandable = true };
        root.children = [new InspectorNode( "3", "Hero" ) { note = "#3" }];
        byte[] bytes = Inspector.WriteResponse( 9, [root] );
        string text = Encoding.UTF8.GetString( bytes );

        Assert.EndsWith( "\n", text );
        Assert.Equal( 1, text.Count( c => c == '\n' ) );

        using var document = JsonDocument.Parse( text );
        var json = document.RootElement;
        Assert.Equal( 9, json.GetProperty( "id" ).GetInt64() );
        var world = json.GetProperty( "roots" )[0];
        Assert.Equal( "world-0", world.GetProperty( "k" ).GetString() );
        Assert.True( world.GetProperty( "s" ).GetBoolean() );
        Assert.True( world.GetProperty( "x" ).GetBoolean() );
        var hero = world.GetProperty( "c" )[0];
        Assert.Equal( "#3", hero.GetProperty( "n" ).GetString() );
        Assert.False( hero.TryGetProperty( "v", out _ ) );
        Assert.False( hero.TryGetProperty( "x", out _ ) );
    }

    [Fact]
    public void ASourceThatThrowsIsShownRatherThanFailingTheTree ()
    {
        var source = new Throwing();
        Inspector.Register( source );
        try
        {
            var roots = Inspector.Snapshot( InspectorQuery.Nothing );
            var row = Assert.Single( roots, root => root.label == nameof( Throwing ) );
            Assert.Contains( "boom", row.value );
        }
        finally
        {
            Inspector.Unregister( source );
        }
    }

    sealed class Throwing : IInspectable
    {
        public InspectorNode Inspect ( InspectorQuery query ) => throw new InvalidOperationException( "boom" );
    }

    [Fact]
    public void TheGameAnswersOverTheSocket ()
    {
        string path = Path.Combine( Path.GetTempPath(), $"engine-inspector-{Guid.NewGuid():N}.sock".Substring( 0, 32 ) );
        using var listener = new Socket( AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified );
        listener.Bind( new UnixDomainSocketEndPoint( path ) );
        listener.Listen( 1 );

        var garage = new Garage();
        Inspector.Register( garage );
        try
        {
            Assert.Null( Inspector.Connect( path ) );
            using var tool = listener.Accept();
            using var stream = new NetworkStream( tool );
            using var reader = new StreamReader( stream, Encoding.UTF8 );

            using ( var hello = JsonDocument.Parse( reader.ReadLine()! ) )
                Assert.Equal( Inspector.PROTOCOL_VERSION, hello.RootElement.GetProperty( "protocol" ).GetInt32() );

            stream.Write( """{"id":1,"expanded":["garage"]}"""u8.ToArray().Concat( "\n"u8.ToArray() ).ToArray() );
            string? reply = null;
            var deadline = DateTime.UtcNow.AddSeconds( 5 );
            var read = Task.Run( reader.ReadLine );
            while ( !read.IsCompleted && DateTime.UtcNow < deadline )
            {
                Inspector.Pump();
                Thread.Sleep( 10 );
            }

            reply = read.Result;
            using var document = JsonDocument.Parse( reply! );
            var root = document.RootElement.GetProperty( "roots" ).EnumerateArray()
                .Single( node => node.GetProperty( "k" ).GetString() == "garage" );
            var fields = root.GetProperty( "c" ).EnumerateArray().Select( node => node.GetProperty( "k" ).GetString() );
            Assert.Equal( ["name", "speed", "position", "tags"], fields );
        }
        finally
        {
            Inspector.Stop();
            Inspector.Unregister( garage );
            File.Delete( path );
        }
    }
}
