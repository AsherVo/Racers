using System.Runtime.InteropServices;
using Engine.Native;

namespace Engine;

/// <summary>
/// Writes through SDL's default log output, so messages land in each platform's native log
/// (logcat on Android, os_log on Apple platforms, stderr on desktop). The browser uses the console.
/// </summary>
public static unsafe class Log
{
    public static void Info(string message) => Write(SDL.LOG_PRIORITY_INFO, message);

    public static void Error(string message) => Write(SDL.LOG_PRIORITY_ERROR, "ERROR: " + message);

    static void Write(int priority, string message)
    {
        if (OperatingSystem.IsBrowser())
        {
            Console.WriteLine(message);
            return;
        }

        // SDL_Log is variadic, which P/Invoke can't call portably; the output function is not.
        var output = SDL.SDL_GetDefaultLogOutputFunction();
        nint utf8 = Marshal.StringToCoTaskMemUTF8(message);
        try
        {
            output(null, SDL.LOG_CATEGORY_APPLICATION, priority, (byte*)utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }
}
