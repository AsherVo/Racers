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
    const string LIB = "SDL3";

    // SDL_init.h
    public const uint INIT_VIDEO = 0x20;
    public const uint INIT_EVENTS = 0x4000;

    // SDL_hints.h
    public const string HINT_ORIENTATIONS = "SDL_ORIENTATIONS"; // iOS/Android; set before SDL_Init

    // SDL_video.h
    public const ulong WINDOW_FULLSCREEN = 0x1;
    public const ulong WINDOW_OPENGL = 0x2;
    public const ulong WINDOW_RESIZABLE = 0x20;
    public const ulong WINDOW_HIGH_PIXEL_DENSITY = 0x2000;
    public const ulong WINDOW_FILL_DOCUMENT = 0x200000; // Emscripten only
    public const int GL_CONTEXT_MAJOR_VERSION = 17;      // SDL_GLAttr
    public const int GL_CONTEXT_MINOR_VERSION = 18;
    public const int GL_CONTEXT_PROFILE_MASK = 20;
    public const int GL_CONTEXT_PROFILE_ES = 0x4;

    // SDL_keycode.h / SDL_scancode.h
    public const ushort KMOD_ALT = 0x0300; // either Alt (Option on macOS)
    public const uint SCANCODE_RETURN = 40;
    public const uint SCANCODE_KP_ENTER = 88;

    // SDL_render.h / SDL_surface.h / SDL_pixels.h
    public const int LOGICAL_PRESENTATION_LETTERBOX = 2;
    public const int LOGICAL_PRESENTATION_INTEGER_SCALE = 4;
    public const int SCALEMODE_LINEAR = 1;
    public const int SCALEMODE_PIXELART = 2;
    public const int TEXTUREACCESS_STATIC = 0;
    public const uint PIXELFORMAT_ABGR8888 = 0x16762004;
    public const uint PIXELFORMAT_ARGB8888 = 0x16362004;
    public const string PROP_TEXTURE_CREATE_FORMAT_NUMBER = "SDL.texture.create.format";
    public const string PROP_TEXTURE_CREATE_WIDTH_NUMBER = "SDL.texture.create.width";
    public const string PROP_TEXTURE_CREATE_HEIGHT_NUMBER = "SDL.texture.create.height";
    public const string PROP_TEXTURE_CREATE_METAL_PIXELBUFFER_POINTER = "SDL.texture.create.metal.pixelbuffer";
    public const string PROP_TEXTURE_OPENGLES2_TEXTURE_NUMBER = "SDL.texture.opengles2.texture";

    // SDL_blendmode.h
    public const uint BLENDMODE_BLEND = 0x1;
    public const uint BLENDMODE_BLEND_PREMULTIPLIED = 0x10;

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
    public const uint EVENT_WINDOW_PIXEL_SIZE_CHANGED = 0x207;
    public const uint EVENT_KEY_DOWN = 0x300;
    public const uint EVENT_KEY_UP = 0x301;
    public const uint EVENT_MOUSE_MOTION = 0x400;
    public const uint EVENT_MOUSE_BUTTON_DOWN = 0x401;
    public const uint EVENT_MOUSE_BUTTON_UP = 0x402;

    /// <summary>SDL_Event: a 128-byte union. Only the members the engine reads are declared.</summary>
    [StructLayout( LayoutKind.Explicit, Size = 128 )]
    public struct Event
    {
        [FieldOffset( 0 )] public uint type;
        [FieldOffset( 0 )] public KeyboardEvent key;
        [FieldOffset( 0 )] public MouseMotionEvent motion;
        [FieldOffset( 0 )] public MouseButtonEvent button;
    }

    public struct KeyboardEvent
    {
        public uint type, reserved;
        public ulong timestamp;
        public uint windowId, which, scancode, keycode;
        public ushort mod, raw;
        public byte down, repeat;
    }

    public struct MouseMotionEvent
    {
        public uint type, reserved;
        public ulong timestamp;
        public uint windowId, which, state;
        public float x, y, xRel, yRel;
    }

    public struct MouseButtonEvent
    {
        public uint type, reserved;
        public ulong timestamp;
        public uint windowId, which;
        public byte button, down, clicks, padding;
        public float x, y;
    }

    public struct Rect
    {
        public int x, y, w, h;
    }

    public struct FRect
    {
        public float x, y, w, h;
    }

    /// <summary>SDL_Vertex: position, color (floats), normalized texture coordinate.</summary>
    public struct Vertex
    {
        public float x, y;
        public float r, g, b, a;
        public float u, v;
    }

    // Main / lifecycle
    [LibraryImport( LIB )]
    public static partial int SDL_RunApp ( int argc, byte** argv, delegate* unmanaged[Cdecl]< int, byte**, int > mainFunction, void* reserved );

    [LibraryImport( LIB )]
    public static partial int SDL_EnterAppMainCallbacks ( int argc, byte** argv,
        delegate* unmanaged[Cdecl]< nint*, int, byte**, int > appInit,
        delegate* unmanaged[Cdecl]< nint, int > appIterate,
        delegate* unmanaged[Cdecl]< nint, Event*, int > appEvent,
        delegate* unmanaged[Cdecl]< nint, int, void > appQuit );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_Init ( uint flags );

    [LibraryImport( LIB )]
    public static partial void SDL_Quit ();

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetHint ( string name, string value );

    [LibraryImport( LIB )]
    public static partial byte* SDL_GetError ();

    [LibraryImport( LIB )]
    public static partial byte* SDL_GetPlatform ();

    [LibraryImport( LIB )]
    public static partial byte* SDL_GetBasePath ();

    [LibraryImport( LIB )]
    public static partial ulong SDL_GetTicksNS ();

    [LibraryImport( LIB )]
    public static partial delegate* unmanaged[Cdecl]< void*, int, int, byte*, void > SDL_GetDefaultLogOutputFunction ();

    // Events
    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_PollEvent ( Event* e );

    // Window and renderer
    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_CreateWindowAndRenderer ( string title, int width, int height, ulong windowFlags, out nint window, out nint renderer );

    [LibraryImport( LIB )]
    public static partial void SDL_DestroyWindow ( nint window );

    [LibraryImport( LIB )]
    public static partial uint SDL_GetPrimaryDisplay ();

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_GetDisplayUsableBounds ( uint displayId, out Rect rect );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_GetWindowSize ( nint window, out int w, out int h );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_GL_SetAttribute ( int attr, int value );

    [LibraryImport( LIB )]
    public static partial ulong SDL_GetWindowFlags ( nint window );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetWindowFullscreen ( nint window, [MarshalAs( UnmanagedType.U1 )] bool fullscreen );

    [LibraryImport( LIB )]
    public static partial void SDL_DestroyRenderer ( nint renderer );

    [LibraryImport( LIB )]
    public static partial byte* SDL_GetRendererName ( nint renderer );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetRenderVSync ( nint renderer, int vsync );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetRenderLogicalPresentation ( nint renderer, int w, int h, int mode );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_GetRenderLogicalPresentationRect ( nint renderer, out FRect rect );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_GetRenderOutputSize ( nint renderer, out int w, out int h );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_ConvertEventToRenderCoordinates ( nint renderer, Event* e );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetRenderDrawColorFloat ( nint renderer, float r, float g, float b, float a );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_RenderClear ( nint renderer );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_RenderFillRect ( nint renderer, in FRect rect );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_RenderGeometry ( nint renderer, nint texture, Vertex* vertices, int numVertices, int* indices, int numIndices );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_RenderPresent ( nint renderer );

    [LibraryImport( LIB )]
    public static partial nint SDL_RenderReadPixels ( nint renderer, void* rect );

    [LibraryImport( LIB )]
    public static partial nint SDL_GetRenderMetalLayer ( nint renderer );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_FlushRenderer ( nint renderer );

    // Textures and surfaces
    [LibraryImport( LIB )]
    public static partial nint SDL_CreateTexture ( nint renderer, uint format, int access, int w, int h );

    [LibraryImport( LIB )]
    public static partial nint SDL_CreateTextureFromSurface ( nint renderer, nint surface );

    [LibraryImport( LIB )]
    public static partial nint SDL_CreateTextureWithProperties ( nint renderer, uint props );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetTextureBlendMode ( nint texture, uint blendMode );

    [LibraryImport( LIB )]
    public static partial uint SDL_GetTextureProperties ( nint texture );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_UpdateTexture ( nint texture, void* rect, void* pixels, int pitch );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_GetTextureSize ( nint texture, out float w, out float h );

    [LibraryImport( LIB )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetTextureScaleMode ( nint texture, int scaleMode );

    [LibraryImport( LIB )]
    public static partial void SDL_DestroyTexture ( nint texture );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial nint SDL_LoadPNG ( string file );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SavePNG ( nint surface, string file );

    [LibraryImport( LIB )]
    public static partial void SDL_DestroySurface ( nint surface );

    // Properties
    [LibraryImport( LIB )]
    public static partial uint SDL_CreateProperties ();

    [LibraryImport( LIB )]
    public static partial void SDL_DestroyProperties ( uint props );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetPointerProperty ( uint props, string name, nint value );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    [return: MarshalAs( UnmanagedType.U1 )]
    public static partial bool SDL_SetNumberProperty ( uint props, string name, long value );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial long SDL_GetNumberProperty ( uint props, string name, long defaultValue );

    // Files and memory
    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial void* SDL_LoadFile ( string file, out nuint dataSize );

    [LibraryImport( LIB )]
    public static partial void SDL_free ( void* mem );

    // Helpers

    /// <summary>Copies an SDL-owned UTF-8 string; SDL keeps ownership.</summary>
    public static string? Utf8 ( byte* s ) => Marshal.PtrToStringUTF8( ( nint )s );

    public static string GetError () => Utf8( SDL_GetError() ) ?? "";

    public static string GetPlatform () => Utf8( SDL_GetPlatform() ) ?? "";
}
