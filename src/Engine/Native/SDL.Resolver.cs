using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Engine.Native;

internal static partial class SDL
{
    /// <summary>
    /// On iOS/tvOS, SDL is linked statically into the app executable, so there is no "SDL3" library
    /// to load; point the engine's imports at the main program instead. Elsewhere the default
    /// probing finds SDL3.dll / libSDL3.dylib / libSDL3.so, or the browser's static link.
    /// </summary>
    // Must run before any SDL call from any host, without each host having to remember it.
#pragma warning disable CA2255 // ModuleInitializer in a library
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void RegisterResolver ()
    {
        if ( !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS() )
            return;

        NativeLibrary.SetDllImportResolver( typeof( SDL ).Assembly, static ( name, _, _ ) =>
            name == LIB ? NativeLibrary.GetMainProgramHandle() : 0 );
    }
}
