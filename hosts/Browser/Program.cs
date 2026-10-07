using System.Runtime.InteropServices.JavaScript;
using Engine;
using Racers;

namespace Browser;

/// <summary>
/// The browser can't run a blocking loop, so main.js drives the game: it calls <see cref="Init"/>
/// once, then <see cref="Frame"/> from requestAnimationFrame.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("browser")]
public static partial class Program
{
    static GameRunner? s_runner;

    // Required for an Exe; the JS side calls Init instead of running Main.
    public static void Main() { }

    [JSExport]
    internal static bool Init()
    {
        s_runner = new GameRunner(new RacersGame(), RacersGame.Options);
        return s_runner.Init();
    }

    [JSExport]
    internal static bool Frame()
    {
        if (s_runner is null)
            return false;

        if (s_runner.Frame())
            return true;

        s_runner.Shutdown();
        s_runner = null;
        return false;
    }
}
