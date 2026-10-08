using System.Runtime.InteropServices;

namespace Engine.Native;

/// <summary>
/// Binding for libEngineNative (native/include/engine_native.h): font baking and Rive. Built by
/// tools/build-native-macos.sh (Metal) and tools/build-native-wasm.sh (WebGL 2).
/// </summary>
internal static unsafe partial class EngineNative
{
    const string LIB = "EngineNative";

    public struct Glyph
    {
        public ushort x0, y0, x1, y1;
        public float xOffset, yOffset, xAdvance, xOffset2, yOffset2;
    }

    public struct FontMetrics
    {
        public float ascent, descent, lineGap;
    }

    [LibraryImport( LIB )]
    public static partial int en_font_bake ( byte* ttf, int ttfLength, float pixelHeight, int firstCodepoint, int count,
        byte* atlas, int width, int height, Glyph* glyphs, float* kerning, FontMetrics* metrics );

    public const int RIVE_METAL = 1, RIVE_WEBGL = 2;

    [LibraryImport( LIB )]
    public static partial int en_rive_backend ();

    [LibraryImport( LIB )]
    public static partial nint en_rive_context_create ( nint metalLayer );

    [LibraryImport( LIB )]
    public static partial void en_rive_context_destroy ( nint context );

    [LibraryImport( LIB )]
    public static partial nint en_rive_file_load ( nint context, byte* bytes, int length );

    [LibraryImport( LIB )]
    public static partial void en_rive_file_destroy ( nint file );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial nint en_rive_instance_create ( nint file, string? artboard, string? stateMachine, out float width, out float height );

    [LibraryImport( LIB )]
    public static partial void en_rive_instance_destroy ( nint instance );

    [LibraryImport( LIB )]
    public static partial int en_rive_instance_advance ( nint instance, float seconds );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial int en_rive_instance_set_number ( nint instance, string name, float value );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial int en_rive_instance_set_bool ( nint instance, string name, int value );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial int en_rive_instance_fire ( nint instance, string name );

    [LibraryImport( LIB, StringMarshalling = StringMarshalling.Utf8 )]
    public static partial int en_rive_instance_set_text ( nint instance, string run, string text );

    [LibraryImport( LIB )]
    public static partial int en_rive_instance_pointer ( nint instance, int action, float x, float y );

    [LibraryImport( LIB )]
    public static partial nint en_rive_target_create ( nint context, int width, int height, uint glTexture, out nint pixelBuffer );

    [LibraryImport( LIB )]
    public static partial void en_rive_target_destroy ( nint target );

    [LibraryImport( LIB )]
    public static partial void en_rive_render ( nint context, nint instance, nint target );

    [LibraryImport( LIB )]
    public static partial void en_rive_finish ( nint context );
}
