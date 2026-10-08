using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine.Native;

namespace Engine;

/// <summary>
/// Runs a game through SDL's main-callback lifecycle, mirroring what SDL_main.h does for C apps:
/// SDL_RunApp → main → SDL_EnterAppMainCallbacks. This is the entry point for every host except
/// the browser, which drives <see cref="GameRunner.Frame"/> from requestAnimationFrame.
/// </summary>
/// <remarks>
/// On desktop and Android, SDL_EnterAppMainCallbacks runs the loop and returns when the game ends.
/// On iOS, SDL_RunApp enters UIApplicationMain and SDL_EnterAppMainCallbacks returns immediately,
/// leaving a CADisplayLink to call the callbacks. Static [UnmanagedCallersOnly] callbacks work for
/// both, with nothing to keep alive.
/// </remarks>
public static unsafe class GameHost
{
    static GameRunner? Runner;

    public static int Run ( Func< Game > createGame, GameOptions options )
    {
        if ( Runner is not null )
            throw new InvalidOperationException( "A game is already running." );

        Runner = new GameRunner( createGame(), options );
        return SDL.SDL_RunApp( 0, null, &Main, null );
    }

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    static int Main ( int argc, byte** argv ) =>
        SDL.SDL_EnterAppMainCallbacks( argc, argv, &AppInit, &AppIterate, &AppEvent, &AppQuit );

    // Exceptions must not unwind through SDL's native frames, so each callback catches.

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    static int AppInit ( nint* appState, int argc, byte** argv )
    {
        try
        {
            return Runner!.Init() ? SDL.APP_CONTINUE : SDL.APP_FAILURE;
        }
        catch ( Exception ex )
        {
            return Crash( ex );
        }
    }

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    static int AppIterate ( nint appState )
    {
        try
        {
            return Runner!.Iterate() ? SDL.APP_CONTINUE : SDL.APP_SUCCESS;
        }
        catch ( Exception ex )
        {
            return Crash( ex );
        }
    }

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    static int AppEvent ( nint appState, SDL.Event* e )
    {
        try
        {
            return Runner!.HandleEvent( ref *e ) ? SDL.APP_CONTINUE : SDL.APP_SUCCESS;
        }
        catch ( Exception ex )
        {
            return Crash( ex );
        }
    }

    [UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
    static void AppQuit ( nint appState, int result )
    {
        try
        {
            Runner?.Shutdown();
        }
        catch ( Exception ex )
        {
            Crash( ex );
        }

        Runner = null;
    }

    static int Crash ( Exception ex )
    {
        Log.Error( $"Unhandled exception: {ex}" );
        return SDL.APP_FAILURE;
    }
}
