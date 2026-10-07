using System.Runtime.InteropServices;

namespace Engine.Native;

/// <summary>
/// The slice of SDL 3.4 the engine uses, bound directly: blittable [LibraryImport] calls, no
/// delegates, no reflection. This compiles under every AOT the hosts use (NativeAOT on desktop and
/// iOS, Mono AOT for WebAssembly), which the general-purpose SDL3-CS bindings do not.
/// Constants and layouts are checked against the SDL 3.4.18 headers; add functions as needed.
/// </summary>
internal static unsafe partial class SDL
{
    const string Lib = "SDL3";

    // SDL_init.h
    public const uint INIT_VIDEO = 0x20;
    public const uint INIT_EVENTS = 0x4000;

    // SDL_hints.h
    public const string HINT_ORIENTATIONS = "SDL_ORIENTATIONS"; // iOS/Android; set before SDL_Init

    // SDL_video.h
    public const ulong WINDOW_FULLSCREEN = 0x1;
    public const ulong WINDOW_RESIZABLE = 0x20;
    public const ulong WINDOW_HIGH_PIXEL_DENSITY = 0x2000;
    public const ulong WINDOW_FILL_DOCUMENT = 0x200000; // Emscripten only

    // SDL_keycode.h / SDL_scancode.h
    public const ushort KMOD_ALT = 0x0300; // either Alt (Option on macOS)
    public const uint SCANCODE_RETURN = 40;
    public const uint SCANCODE_KP_ENTER = 88;

    // SDL_render.h / SDL_surface.h / SDL_pixels.h
    public const int LOGICAL_PRESENTATION_LETTERBOX = 2;
    public const int SCALEMODE_LINEAR = 1;
    public const int SCALEMODE_PIXELART = 2;
    public const int TEXTUREACCESS_STATIC = 0;
    public const uint PIXELFORMAT_ABGR8888 = 0x16762004;

    // SDL_log.h
    public const int LOG_CATEGORY_APPLICATION = 0;
    public const int LOG_PRIORITY_INFO = 4;
    public const int LOG_PRIORITY_ERROR = 6;

    // SDL_main.h
    public const int APP_CONTINUE = 0;
    public const int APP_SUCCESS = 1;
    public const int APP_FAILURE = 2;

    // SDL_events.h
    public const uint EVENT_QUIT = 0x100;
    public const uint EVENT_TERMINATING = 0x101;
    public const uint EVENT_WILL_ENTER_BACKGROUND = 0x103;
    public const uint EVENT_DID_ENTER_FOREGROUND = 0x106;
    public const uint EVENT_KEY_DOWN = 0x300;
    public const uint EVENT_KEY_UP = 0x301;
    public const uint EVENT_MOUSE_MOTION = 0x400;
    public const uint EVENT_MOUSE_BUTTON_DOWN = 0x401;
    public const uint EVENT_MOUSE_BUTTON_UP = 0x402;

    /// <summary>SDL_Event: a 128-byte union. Only the members the engine reads are declared.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct Event
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(0)] public KeyboardEvent Key;
        [FieldOffset(0)] public MouseMotionEvent Motion;
        [FieldOffset(0)] public MouseButtonEvent Button;
    }

    public struct KeyboardEvent
    {
        public uint Type, Reserved;
        public ulong Timestamp;
        public uint WindowId, Which, Scancode, Keycode;
        public ushort Mod, Raw;
        public byte Down, Repeat;
    }

    public struct MouseMotionEvent
    {
        public uint Type, Reserved;
        public ulong Timestamp;
        public uint WindowId, Which, State;
        public float X, Y, XRel, YRel;
    }

    public struct MouseButtonEvent
    {
        public uint Type, Reserved;
        public ulong Timestamp;
        public uint WindowId, Which;
        public byte Button, Down, Clicks, Padding;
        public float X, Y;
    }

    /// <summary>SDL_Vertex: position, color (floats), normalized texture coordinate.</summary>
    public struct Vertex
    {
        public float X, Y;
        public float R, G, B, A;
        public float U, V;
    }

    // Main / lifecycle
    [LibraryImport(Lib)]
    public static partial int SDL_RunApp(int argc, byte** argv, delegate* unmanaged[Cdecl]<int, byte**, int> mainFunction, void* reserved);

    [LibraryImport(Lib)]
    public static partial int SDL_EnterAppMainCallbacks(int argc, byte** argv,
        delegate* unmanaged[Cdecl]<nint*, int, byte**, int> appInit,
        delegate* unmanaged[Cdecl]<nint, int> appIterate,
        delegate* unmanaged[Cdecl]<nint, Event*, int> appEvent,
        delegate* unmanaged[Cdecl]<nint, int, void> appQuit);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_Init(uint flags);

    [LibraryImport(Lib)]
    public static partial void SDL_Quit();

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetHint(string name, string value);

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetError();

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetPlatform();

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetBasePath();

    [LibraryImport(Lib)]
    public static partial ulong SDL_GetTicksNS();

    [LibraryImport(Lib)]
    public static partial delegate* unmanaged[Cdecl]<void*, int, int, byte*, void> SDL_GetDefaultLogOutputFunction();

    // Events
    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_PollEvent(Event* e);

    // Window and renderer
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_CreateWindowAndRenderer(string title, int width, int height, ulong windowFlags, out nint window, out nint renderer);

    [LibraryImport(Lib)]
    public static partial void SDL_DestroyWindow(nint window);

    [LibraryImport(Lib)]
    public static partial ulong SDL_GetWindowFlags(nint window);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetWindowFullscreen(nint window, [MarshalAs(UnmanagedType.U1)] bool fullscreen);

    [LibraryImport(Lib)]
    public static partial void SDL_DestroyRenderer(nint renderer);

    [LibraryImport(Lib)]
    public static partial byte* SDL_GetRendererName(nint renderer);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetRenderVSync(nint renderer, int vsync);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetRenderLogicalPresentation(nint renderer, int w, int h, int mode);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_ConvertEventToRenderCoordinates(nint renderer, Event* e);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetRenderDrawColorFloat(nint renderer, float r, float g, float b, float a);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_RenderClear(nint renderer);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_RenderGeometry(nint renderer, nint texture, Vertex* vertices, int numVertices, int* indices, int numIndices);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_RenderPresent(nint renderer);

    [LibraryImport(Lib)]
    public static partial nint SDL_RenderReadPixels(nint renderer, void* rect);

    // Textures and surfaces
    [LibraryImport(Lib)]
    public static partial nint SDL_CreateTexture(nint renderer, uint format, int access, int w, int h);

    [LibraryImport(Lib)]
    public static partial nint SDL_CreateTextureFromSurface(nint renderer, nint surface);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_UpdateTexture(nint texture, void* rect, void* pixels, int pitch);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_GetTextureSize(nint texture, out float w, out float h);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SetTextureScaleMode(nint texture, int scaleMode);

    [LibraryImport(Lib)]
    public static partial void SDL_DestroyTexture(nint texture);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint SDL_LoadPNG(string file);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool SDL_SavePNG(nint surface, string file);

    [LibraryImport(Lib)]
    public static partial void SDL_DestroySurface(nint surface);

    // Helpers

    /// <summary>Copies an SDL-owned UTF-8 string; SDL keeps ownership.</summary>
    public static string? Utf8(byte* s) => Marshal.PtrToStringUTF8((nint)s);

    public static string GetError() => Utf8(SDL_GetError()) ?? "";

    public static string GetPlatform() => Utf8(SDL_GetPlatform()) ?? "";
}
