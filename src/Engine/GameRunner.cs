using System.Numerics;
using Engine.Native;

namespace Engine;

/// <summary>
/// Platform-agnostic frame driver. Hosts call <see cref="Init"/> once, then either let SDL's main
/// callbacks drive <see cref="Iterate"/>/<see cref="HandleEvent"/> (desktop, iOS, Android), or call
/// <see cref="Frame"/> from an external loop such as the browser's requestAnimationFrame.
/// </summary>
public sealed unsafe class GameRunner ( Game game, GameOptions options )
{
    nint window;
    nint renderer;
    GameConfig config = null!;
    SpriteBatch batch = null!;
    ulong lastTicks;
    double totalSeconds;
    bool quit;
    bool paused;
    int frameCount;

    // Debug aid: ENGINE_CAPTURE=/path/to/frame.png saves one frame once the scene has settled.
    const int CAPTURE_FRAME = 90;
    readonly string? capturePath = Environment.GetEnvironmentVariable( "ENGINE_CAPTURE" );

    public bool Init ()
    {
        bool mobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS();
        if ( mobile )
        {
            SDL.SDL_SetHint( SDL.HINT_ORIENTATIONS, options.orientation switch
            {
                Orientation.Landscape => "LandscapeLeft LandscapeRight",
                Orientation.Portrait => "Portrait",
                _ => "LandscapeLeft LandscapeRight Portrait PortraitUpsideDown",
            } );
        }

        if ( !SDL.SDL_Init( SDL.INIT_VIDEO | SDL.INIT_EVENTS ) )
            return Fail( "SDL_Init" );

        string contentRoot = options.contentRoot ?? ContentManager.DefaultRoot();
        config = options.configPath is null ? new GameConfig() : ContentManager.ReadYaml< GameConfig >( contentRoot, options.configPath );
        config.Validate( options.configPath ?? nameof( GameConfig ) );

        ulong flags = SDL.WINDOW_RESIZABLE | SDL.WINDOW_HIGH_PIXEL_DENSITY;
        if ( OperatingSystem.IsBrowser() )
        {
            // Ask for WebGL 2 (GLES 3.0), which Rive needs. SDL's GLES2 renderer keeps these
            // attributes only if the window is already an OpenGL one; otherwise it asks for WebGL 1.
            flags |= SDL.WINDOW_FILL_DOCUMENT | SDL.WINDOW_OPENGL;
            SDL.SDL_GL_SetAttribute( SDL.GL_CONTEXT_PROFILE_MASK, SDL.GL_CONTEXT_PROFILE_ES );
            SDL.SDL_GL_SetAttribute( SDL.GL_CONTEXT_MAJOR_VERSION, 3 );
            SDL.SDL_GL_SetAttribute( SDL.GL_CONTEXT_MINOR_VERSION, 0 );
        }
        else if ( mobile )
            flags |= SDL.WINDOW_FULLSCREEN; // hides the status and navigation bars

        var ( windowWidth, windowHeight ) = InitialWindowSize();
        if ( !SDL.SDL_CreateWindowAndRenderer( config.title, windowWidth, windowHeight, flags, out window, out renderer ) )
            return Fail( "SDL_CreateWindowAndRenderer" );

        // In the browser, requestAnimationFrame already paces frames to the display.
        if ( !OperatingSystem.IsBrowser() && !SDL.SDL_SetRenderVSync( renderer, 1 ) )
            Log.Error( $"SDL_SetRenderVSync failed: {SDL.GetError()}" );

        Log.Info( $"Platform: {SDL.GetPlatform()}, renderer: {SDL.Utf8( SDL.SDL_GetRendererName( renderer ) )}" );

        game.graphics = new Graphics( renderer );
        if ( !UpdateView() )
        {
            // No usable window size yet; show the configured resolution until the first resize event.
            SDL.SDL_SetRenderLogicalPresentation( renderer, config.resolutionX, config.resolutionY, SDL.LOGICAL_PRESENTATION_LETTERBOX );
            game.graphics.SetView( config.resolutionX, config.resolutionY, 1f );
        }
        Log.Info( $"Resolution {game.graphics.width}x{game.graphics.height} at {game.graphics.pixelScale:0.##}x" );

        game.content = new ContentManager( game.graphics, contentRoot );
        batch = new SpriteBatch( game.graphics );

        game.Load();
        lastTicks = SDL.SDL_GetTicksNS();
        return true;
    }

    /// <summary>Polls events and runs one frame. Returns false when the game should stop.</summary>
    public bool Frame ()
    {
        SDL.Event e;
        while ( SDL.SDL_PollEvent( &e ) )
        {
            if ( !HandleEvent( ref e ) )
                return false;
        }

        return Iterate();
    }

    public bool Iterate ()
    {
        ulong now = SDL.SDL_GetTicksNS();
        float dt = ( now - lastTicks ) / 1e9f;
        lastTicks = now;

        if ( paused )
            return !quit;

        // Clamp so a debugger break or a backgrounded tab doesn't produce a huge step.
        dt = MathF.Min( dt, 0.1f );
        totalSeconds += dt;
        game.Update( new GameTime( totalSeconds, dt ) );
        game.graphics.UpdateRive( dt );

        // RenderClear covers the whole window, so the bars around the game stay black.
        SDL.SDL_SetRenderDrawColorFloat( renderer, 0f, 0f, 0f, 1f );
        SDL.SDL_RenderClear( renderer );
        var c = options.clearColor;
        SDL.SDL_SetRenderDrawColorFloat( renderer, c.r, c.g, c.b, c.a );
        SDL.SDL_RenderFillRect( renderer, new SDL.FRect { w = game.graphics.width, h = game.graphics.height } );

        batch.Begin();
        game.Draw( batch );
        batch.End();

        if ( ++frameCount == CAPTURE_FRAME && capturePath is not null )
            Capture( capturePath );

        SDL.SDL_RenderPresent( renderer );
        return !( quit || game.exitRequested );
    }

    /// <summary>Returns false when the event means the game should stop.</summary>
    internal bool HandleEvent ( ref SDL.Event e )
    {
        switch ( e.type )
        {
            case SDL.EVENT_QUIT:
            case SDL.EVENT_TERMINATING:
                quit = true;
                return false;

            case SDL.EVENT_WILL_ENTER_BACKGROUND:
                paused = true;
                break;

            case SDL.EVENT_DID_ENTER_FOREGROUND:
                paused = false;
                lastTicks = SDL.SDL_GetTicksNS();
                break;

            case SDL.EVENT_WINDOW_PIXEL_SIZE_CHANGED:
                if ( UpdateView() )
                    game.OnResize();
                break;

            // SDL synthesizes mouse events from touch by default, so this covers mobile too.
            case SDL.EVENT_MOUSE_MOTION:
                fixed ( SDL.Event* p = &e )
                    SDL.SDL_ConvertEventToRenderCoordinates( renderer, p );
                game.OnPointer( new PointerEvent( PointerAction.Move, ToGame( e.motion.x, e.motion.y ) ) );
                break;

            case SDL.EVENT_MOUSE_BUTTON_DOWN:
            case SDL.EVENT_MOUSE_BUTTON_UP:
                fixed ( SDL.Event* p = &e )
                    SDL.SDL_ConvertEventToRenderCoordinates( renderer, p );
                var action = e.button.down != 0 ? PointerAction.Down : PointerAction.Up;
                game.OnPointer( new PointerEvent( action, ToGame( e.button.x, e.button.y ) ) );
                break;

            case SDL.EVENT_KEY_DOWN when IsFullscreenToggle( ref e.key ):
                if ( e.key.repeat == 0 )
                    ToggleFullscreen();
                break;

            case SDL.EVENT_KEY_DOWN:
            case SDL.EVENT_KEY_UP:
                if ( e.key.repeat == 0 )
                    game.OnKey( ( Key )e.key.scancode, e.key.down != 0 );
                break;
        }

        return !game.exitRequested;
    }

    /// <summary>Converts SDL render coordinates (top-left origin, y down) to game pixels (bottom-left origin, y up).</summary>
    Vector2 ToGame ( float x, float y ) => new( x, game.graphics.height - y );

    ( int Width, int Height ) InitialWindowSize ()
    {
        // Mobile windows are fullscreen and the browser's fills the page, so only the desktop picks a size.
        if ( !Desktop || !SDL.SDL_GetDisplayUsableBounds( SDL.SDL_GetPrimaryDisplay(), out var usable ) )
            usable = default;

        return ViewFit.InitialWindowSize( config, usable.w, usable.h );
    }

    /// <summary>Fits the game to the window's current size. Returns whether anything the game sees changed.</summary>
    bool UpdateView ()
    {
        // A minimized window can report a zero size; keep the last view until it's restored.
        if ( !SDL.SDL_GetRenderOutputSize( renderer, out int outputWidth, out int outputHeight )
            || !SDL.SDL_GetWindowSize( window, out int windowWidth, out _ )
            || outputWidth <= 0 || outputHeight <= 0 || windowWidth <= 0 )
            return false;

        var fit = ViewFit.Compute( config, outputWidth, outputHeight, outputWidth / ( float )windowWidth );
        if ( !SDL.SDL_SetRenderLogicalPresentation( renderer, fit.width, fit.height, fit.presentation )
            || !SDL.SDL_GetRenderLogicalPresentationRect( renderer, out var presented ) )
        {
            Log.Error( $"Fitting the game to the window failed: {SDL.GetError()}" );
            return false;
        }

        var graphics = game.graphics;
        float pixelScale = presented.w / fit.width;
        if ( fit.width == graphics.width && fit.height == graphics.height && pixelScale == graphics.pixelScale )
            return false;

        graphics.SetView( fit.width, fit.height, pixelScale );
        return true;
    }

    static readonly bool Desktop = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    /// <summary>Alt+Enter (Option+Return on macOS), desktop only. Mobile is always fullscreen and the browser fills the page.</summary>
    static bool IsFullscreenToggle ( ref SDL.KeyboardEvent key ) =>
        Desktop
        && ( key.mod & SDL.KMOD_ALT ) != 0
        && key.scancode is SDL.SCANCODE_RETURN or SDL.SCANCODE_KP_ENTER;

    /// <summary>Switches between a window and borderless fullscreen at desktop resolution.</summary>
    public void ToggleFullscreen ()
    {
        bool fullscreen = ( SDL.SDL_GetWindowFlags( window ) & SDL.WINDOW_FULLSCREEN ) != 0;
        if ( !SDL.SDL_SetWindowFullscreen( window, !fullscreen ) )
            Log.Error( $"SDL_SetWindowFullscreen failed: {SDL.GetError()}" );
    }

    /// <summary>Saves the current back buffer as a PNG, for automated checks on any platform.</summary>
    public void Capture ( string path )
    {
        nint surface = SDL.SDL_RenderReadPixels( renderer, null );
        if ( surface == 0 || !SDL.SDL_SavePNG( surface, path ) )
            Log.Error( $"Capture to '{path}' failed: {SDL.GetError()}" );
        else
            Log.Info( $"Captured frame {frameCount} to {path}" );

        if ( surface != 0 )
            SDL.SDL_DestroySurface( surface );
    }

    public void Shutdown ()
    {
        game.Unload();
        game.graphics?.Dispose();
        if ( renderer != 0 ) SDL.SDL_DestroyRenderer( renderer );
        if ( window != 0 ) SDL.SDL_DestroyWindow( window );
        SDL.SDL_Quit();
    }

    static bool Fail ( string what )
    {
        Log.Error( $"{what} failed: {SDL.GetError()}" );
        return false;
    }
}
