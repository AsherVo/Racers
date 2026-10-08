using System.Runtime.InteropServices.JavaScript;
using Engine;
using Racers;

namespace Browser;

/// <summary>
/// The browser can't run a blocking loop, so main.js drives the game: it calls <see cref="Init"/>
/// once, then <see cref="Frame"/> from requestAnimationFrame.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform( "browser" )]
public static partial class Program
{
    static GameRunner? Runner;

    // Required for an Exe; the JS side calls Init instead of running Main.
    public static void Main () { }

    [JSExport]
    internal static bool Init ()
    {
        Runner = new GameRunner( new RacersGame(), RacersGame.Options );
        return Runner.Init();
    }

    [JSExport]
    internal static bool Frame ()
    {
        if ( Runner is null )
            return false;

        if ( Runner.Frame() )
            return true;

        Runner.Shutdown();
        Runner = null;
        return false;
    }
}
