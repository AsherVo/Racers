using System.Numerics;
using Engine.Native;

namespace Engine;

/// <summary>A loaded .riv file. Create instances from it to play its artboards.</summary>
public sealed class RiveFile : IDisposable
{
    readonly RiveRuntime runtime;
    nint handle;

    internal RiveFile ( RiveRuntime runtime, nint handle )
    {
        this.runtime = runtime;
        this.handle = handle;
    }

    /// <summary>
    /// Instantiates an artboard and starts its state machine. Instances keep what they need from
    /// the file, so the file can be disposed while they're alive.
    /// </summary>
    /// <param name="artboard">Artboard name, or null for the file's default artboard.</param>
    /// <param name="stateMachine">State machine name, or null for the default (else the first
    /// state machine, else the first animation).</param>
    /// <param name="resolution">Texture pixels per artboard unit. Use the display's pixel density
    /// (2 on Retina) for artboards drawn at their natural size; lower it for large, soft content.</param>
    public RiveInstance CreateInstance ( string? artboard = null, string? stateMachine = null, float resolution = 1f )
    {
        ObjectDisposedException.ThrowIf( handle == 0, this );
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero( resolution );

        nint instance = EngineNative.en_rive_instance_create( handle, artboard, stateMachine, out float width, out float height );
        if ( instance == 0 )
            throw new ArgumentException( $"No artboard '{artboard ?? "(default)"}' with state machine '{stateMachine ?? "(default)"}'." );

        return runtime.Add( new RiveInstance( runtime, instance, width, height, resolution ) );
    }

    public void Dispose ()
    {
        if ( handle == 0 )
            return;

        runtime.Remove( this );
        EngineNative.en_rive_file_destroy( handle );
        handle = 0;
    }
}

/// <summary>
/// A playing Rive artboard. The engine advances it every frame and renders it into a texture, so
/// it draws through <see cref="SpriteBatch"/> like a sprite, in any order with sprites and text.
/// </summary>
public sealed class RiveInstance : IDisposable
{
    readonly RiveRuntime runtime;
    readonly nint[] targets;
    readonly Texture[] textures;
    nint handle;
    int current;
    bool dirty = true;

    /// <summary>Artboard size, in artboard units.</summary>
    public float width { get; }
    public float height { get; }
    public Vector2 size => new( width, height );

    /// <summary>The most recently rendered frame (premultiplied alpha).</summary>
    public Texture texture => textures[current];

    /// <summary>When true, the engine stops advancing the state machine and keeps the last frame.</summary>
    public bool paused { get; set; }

    internal RiveInstance ( RiveRuntime runtime, nint handle, float width, float height, float resolution )
    {
        this.runtime = runtime;
        this.handle = handle;
        this.width = width;
        this.height = height;

        int w = Math.Max( 1, ( int )MathF.Ceiling( width * resolution ) );
        int h = Math.Max( 1, ( int )MathF.Ceiling( height * resolution ) );
        targets = new nint[runtime.buffersPerInstance];
        textures = new Texture[runtime.buffersPerInstance];
        for ( int i = 0; i < targets.Length; i++ )
            ( targets[i], textures[i] ) = runtime.CreateTarget( w, h );

        // Draw the first frame now so the texture is valid even before the next engine update.
        Advance( 0f );
        runtime.BeginRender();
        Render();
        runtime.EndRender();
    }

    /// <summary>Sets a number input on the state machine. Returns false if there's no such input.</summary>
    public bool SetNumber ( string name, float value ) => EngineNative.en_rive_instance_set_number( live, name, value ) != 0;

    /// <summary>Sets a boolean input on the state machine. Returns false if there's no such input.</summary>
    public bool SetBool ( string name, bool value ) => EngineNative.en_rive_instance_set_bool( live, name, value ? 1 : 0 ) != 0;

    /// <summary>Fires a trigger input on the state machine. Returns false if there's no such input.</summary>
    public bool Fire ( string name ) => EngineNative.en_rive_instance_fire( live, name ) != 0;

    /// <summary>Replaces the text of a named text run. Returns false if there's no such run.</summary>
    public bool SetText ( string run, string text )
    {
        dirty = true;
        return EngineNative.en_rive_instance_set_text( live, run, text ) != 0;
    }

    /// <summary>Pointer events for the state machine's listeners, in artboard coordinates. Return true on a hit.</summary>
    public bool PointerDown ( Vector2 position ) => Pointer( 0, position );
    public bool PointerMove ( Vector2 position ) => Pointer( 1, position );
    public bool PointerUp ( Vector2 position ) => Pointer( 2, position );

    bool Pointer ( int action, Vector2 position ) =>
        EngineNative.en_rive_instance_pointer( live, action, position.X, position.Y ) != 0;

    nint live
    {
        get
        {
            ObjectDisposedException.ThrowIf( handle == 0, this );
            return handle;
        }
    }

    /// <summary>Returns true if the frame changed and needs rendering.</summary>
    internal bool Advance ( float seconds )
    {
        if ( paused )
            return dirty;

        bool changed = EngineNative.en_rive_instance_advance( handle, seconds ) != 0;
        return changed || dirty;
    }

    internal void Render ()
    {
        current = ( current + 1 ) % targets.Length;
        EngineNative.en_rive_render( runtime.context, handle, targets[current] );
        dirty = false;
    }

    public void Dispose ()
    {
        if ( handle == 0 )
            return;

        runtime.Remove( this );
        for ( int i = 0; i < targets.Length; i++ )
        {
            textures[i].Dispose();
            EngineNative.en_rive_target_destroy( targets[i] );
        }

        EngineNative.en_rive_instance_destroy( handle );
        handle = 0;
    }
}

/// <summary>
/// Owns the Rive render context and drives every live instance once per frame, between the game's
/// Update and Draw. Created on first use, so games without Rive never load it.
/// </summary>
/// <remarks>
/// Metal (macOS): Rive renders on its own queue into IOSurfaces SDL wraps. WebGL 2 (browser): Rive
/// renders on SDL's own GL context into textures SDL created, so the two take turns with the context.
/// </remarks>
internal sealed unsafe class RiveRuntime : IDisposable
{
    readonly nint renderer;
    readonly int backend;
    readonly List< RiveInstance > instances = [];
    readonly List< RiveFile > files = [];
    bool rendering;

    public nint context { get; private set; }

    /// <summary>
    /// On Metal, Rive renders on its own queue while SDL may still be sampling earlier frames, so
    /// each instance rotates through a few textures instead of overwriting the one on screen. On
    /// WebGL both share one context, so GL orders the work and one texture is enough.
    /// </summary>
    public int buffersPerInstance => backend == EngineNative.RIVE_METAL ? 3 : 1;

    RiveRuntime ( nint renderer, int backend )
    {
        this.renderer = renderer;
        this.backend = backend;
    }

    public static RiveRuntime Create ( nint renderer )
    {
        int backend;
        try
        {
            backend = EngineNative.en_rive_backend();
        }
        catch ( DllNotFoundException )
        {
            throw new PlatformNotSupportedException( "EngineNative isn't built for this platform (see tools/build-native-*.sh)." );
        }

        string rendererName = SDL.Utf8( SDL.SDL_GetRendererName( renderer ) ) ?? "";
        nint layer = 0;
        switch ( backend )
        {
            case EngineNative.RIVE_METAL:
                layer = SDL.SDL_GetRenderMetalLayer( renderer );
                if ( layer == 0 )
                    throw new PlatformNotSupportedException( $"Rive needs SDL's Metal renderer, not '{rendererName}'." );
                break;

            case EngineNative.RIVE_WEBGL:
                if ( rendererName != "opengles2" )
                    throw new PlatformNotSupportedException( $"Rive needs SDL's opengles2 renderer, not '{rendererName}'." );
                break;

            default:
                throw new PlatformNotSupportedException( "This build of EngineNative has no Rive renderer." );
        }

        var runtime = new RiveRuntime( renderer, backend );
        runtime.BeginRender();
        runtime.context = EngineNative.en_rive_context_create( layer );
        runtime.EndRender();
        if ( runtime.context == 0 )
            throw new InvalidOperationException( "Couldn't create the Rive render context." );

        return runtime;
    }

    public RiveFile Load ( ReadOnlySpan< byte > bytes, string name )
    {
        nint file;
        fixed ( byte* p = bytes )
            file = EngineNative.en_rive_file_load( context, p, bytes.Length );

        if ( file == 0 )
            throw new InvalidDataException( $"'{name}' isn't a Rive file this runtime can read." );

        var riveFile = new RiveFile( this, file );
        files.Add( riveFile );
        return riveFile;
    }

    public ( nint Target, Texture Texture ) CreateTarget ( int width, int height ) =>
        backend == EngineNative.RIVE_METAL ? CreateMetalTarget( width, height ) : CreateWebGLTarget( width, height );

    ( nint, Texture ) CreateMetalTarget ( int width, int height )
    {
        nint target = EngineNative.en_rive_target_create( context, width, height, 0, out nint pixelBuffer );
        if ( target == 0 )
            throw new InvalidOperationException( $"Couldn't create a {width}x{height} Rive render target." );

        // SDL wraps the IOSurface Rive draws into, so there's no copy between them.
        uint props = SDL.SDL_CreateProperties();
        SDL.SDL_SetNumberProperty( props, SDL.PROP_TEXTURE_CREATE_FORMAT_NUMBER, SDL.PIXELFORMAT_ARGB8888 );
        SDL.SDL_SetNumberProperty( props, SDL.PROP_TEXTURE_CREATE_WIDTH_NUMBER, width );
        SDL.SDL_SetNumberProperty( props, SDL.PROP_TEXTURE_CREATE_HEIGHT_NUMBER, height );
        SDL.SDL_SetPointerProperty( props, SDL.PROP_TEXTURE_CREATE_METAL_PIXELBUFFER_POINTER, pixelBuffer );
        nint texture = SDL.SDL_CreateTextureWithProperties( renderer, props );
        SDL.SDL_DestroyProperties( props );

        if ( texture == 0 )
        {
            EngineNative.en_rive_target_destroy( target );
            throw new InvalidOperationException( $"SDL couldn't wrap the Rive target: {SDL.GetError()}" );
        }

        return ( target, new Texture( texture, TextureFilter.Linear, premultiplied: true ) );
    }

    ( nint, Texture ) CreateWebGLTarget ( int width, int height )
    {
        // SDL allocates the texture (it would reallocate one it was handed anyway); Rive draws into it.
        nint handle = SDL.SDL_CreateTexture( renderer, SDL.PIXELFORMAT_ABGR8888, SDL.TEXTUREACCESS_STATIC, width, height );
        if ( handle == 0 )
            throw new InvalidOperationException( $"SDL_CreateTexture failed: {SDL.GetError()}" );

        var texture = new Texture( handle, TextureFilter.Linear, premultiplied: true );
        uint glTexture = ( uint )SDL.SDL_GetNumberProperty( SDL.SDL_GetTextureProperties( handle ), SDL.PROP_TEXTURE_OPENGLES2_TEXTURE_NUMBER, 0 );
        nint target = EngineNative.en_rive_target_create( context, width, height, glTexture, out _ );
        if ( glTexture == 0 || target == 0 )
        {
            texture.Dispose();
            throw new InvalidOperationException( $"Couldn't create a {width}x{height} Rive render target." );
        }

        return ( target, texture );
    }

    public RiveInstance Add ( RiveInstance instance )
    {
        instances.Add( instance );
        return instance;
    }

    public void Remove ( RiveInstance instance ) => instances.Remove( instance );

    public void Remove ( RiveFile file ) => files.Remove( file );

    /// <summary>Starts a run of renders. On WebGL, SDL submits its queued GL work first.</summary>
    public void BeginRender ()
    {
        if ( rendering )
            return;

        if ( backend == EngineNative.RIVE_WEBGL )
            SDL.SDL_FlushRenderer( renderer );
        rendering = true;
    }

    /// <summary>
    /// Ends a run of renders. On Metal, waits for them, since SDL samples the textures from another
    /// queue. On WebGL, hands the GL context back and makes SDL forget the state it had cached.
    /// </summary>
    public void EndRender ()
    {
        if ( !rendering )
            return;

        if ( context != 0 )
            EngineNative.en_rive_finish( context );
        if ( backend == EngineNative.RIVE_WEBGL )
            SDL.SDL_FlushRenderer( renderer );
        rendering = false;
    }

    /// <summary>Advances every instance and re-renders the ones that changed.</summary>
    public void Update ( float seconds )
    {
        foreach ( var instance in instances )
        {
            if ( instance.Advance( seconds ) )
            {
                BeginRender();
                instance.Render();
            }
        }

        EndRender();
    }

    public void Dispose ()
    {
        if ( context == 0 )
            return;

        // Everything that holds GPU resources goes before the context that made them.
        for ( int i = instances.Count - 1; i >= 0; i-- )
            instances[i].Dispose();
        for ( int i = files.Count - 1; i >= 0; i-- )
            files[i].Dispose();

        EngineNative.en_rive_context_destroy( context );
        context = 0;
    }
}
