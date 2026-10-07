using System.Runtime.InteropServices;

namespace Engine.Native;

/// <summary>
/// Binding for libEngineNative (native/include/engine_native.h): font baking and Rive. Built by
/// tools/build-native-macos.sh; other platforms don't have it yet.
/// </summary>
internal static unsafe partial class EngineNative
{
    const string Lib = "EngineNative";

    public struct Glyph
    {
        public ushort X0, Y0, X1, Y1;
        public float XOffset, YOffset, XAdvance, XOffset2, YOffset2;
    }

    public struct FontMetrics
    {
        public float Ascent, Descent, LineGap;
    }

    [LibraryImport(Lib)]
    public static partial int en_font_bake(byte* ttf, int ttfLength, float pixelHeight, int firstCodepoint, int count,
        byte* atlas, int width, int height, Glyph* glyphs, float* kerning, FontMetrics* metrics);

    [LibraryImport(Lib)]
    public static partial int en_rive_supported();

    [LibraryImport(Lib)]
    public static partial nint en_rive_context_create(nint metalLayer);

    [LibraryImport(Lib)]
    public static partial void en_rive_context_destroy(nint context);

    [LibraryImport(Lib)]
    public static partial nint en_rive_file_load(nint context, byte* bytes, int length);

    [LibraryImport(Lib)]
    public static partial void en_rive_file_destroy(nint file);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint en_rive_instance_create(nint file, string? artboard, string? stateMachine, out float width, out float height);

    [LibraryImport(Lib)]
    public static partial void en_rive_instance_destroy(nint instance);

    [LibraryImport(Lib)]
    public static partial int en_rive_instance_advance(nint instance, float seconds);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int en_rive_instance_set_number(nint instance, string name, float value);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int en_rive_instance_set_bool(nint instance, string name, int value);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int en_rive_instance_fire(nint instance, string name);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int en_rive_instance_set_text(nint instance, string run, string text);

    [LibraryImport(Lib)]
    public static partial int en_rive_instance_pointer(nint instance, int action, float x, float y);

    [LibraryImport(Lib)]
    public static partial nint en_rive_target_create(nint context, int width, int height, out nint pixelBuffer);

    [LibraryImport(Lib)]
    public static partial void en_rive_target_destroy(nint target);

    [LibraryImport(Lib)]
    public static partial void en_rive_render(nint context, nint instance, nint target);

    [LibraryImport(Lib)]
    public static partial void en_rive_finish(nint context);
}
