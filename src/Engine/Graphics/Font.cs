using System.Drawing;
using System.Numerics;
using Engine.Native;

namespace Engine;

/// <summary>
/// A TrueType font baked at one size into a glyph atlas. Text is drawn as atlas quads through
/// <see cref="SpriteBatch.DrawString"/>, so it batches and sorts like any other sprite.
/// </summary>
/// <remarks>
/// Glyphs are rasterized at <c>size × density</c> and drawn scaled down, so text stays sharp when
/// the virtual resolution is magnified (Retina, fullscreen). Covers printable ASCII for now.
/// </remarks>
public sealed unsafe class Font : IDisposable
{
    const int FirstChar = 32, CharCount = 95;
    const char Fallback = '?';

    readonly EngineNative.Glyph[] _glyphs;
    readonly float[] _kerning;

    public Texture Texture { get; }

    /// <summary>Pixel height the font was loaded at, in virtual-resolution pixels.</summary>
    public float Size { get; }

    /// <summary>Distance from the top of a line to the baseline.</summary>
    public float Ascent { get; }

    /// <summary>Distance between baselines of consecutive lines.</summary>
    public float LineHeight { get; }

    /// <summary>Atlas pixels per virtual pixel.</summary>
    internal float Density { get; }

    Font(Texture texture, EngineNative.Glyph[] glyphs, float[] kerning, EngineNative.FontMetrics metrics, float size, float density)
    {
        Texture = texture;
        _glyphs = glyphs;
        _kerning = kerning;
        Size = size;
        Density = density;
        Ascent = metrics.Ascent / density;
        LineHeight = (metrics.Ascent - metrics.Descent + metrics.LineGap) / density;
    }

    internal static Font Bake(nint renderer, ReadOnlySpan<byte> ttf, float size, float density)
    {
        var glyphs = new EngineNative.Glyph[CharCount];
        var kerning = new float[CharCount * CharCount];
        EngineNative.FontMetrics metrics;

        for (int dim = 256; dim <= 4096; dim *= 2)
        {
            var atlas = new byte[dim * dim];
            int result;
            fixed (byte* ttfPtr = ttf)
            fixed (byte* atlasPtr = atlas)
            fixed (EngineNative.Glyph* glyphPtr = glyphs)
            fixed (float* kerningPtr = kerning)
            {
                result = EngineNative.en_font_bake(ttfPtr, ttf.Length, size * density, FirstChar, CharCount,
                    atlasPtr, dim, dim, glyphPtr, kerningPtr, &metrics);
            }

            if (result < 0)
                throw new InvalidDataException("Not a readable TrueType font.");
            if (result > 0)
                return new Font(CreateAtlasTexture(renderer, atlas, dim), glyphs, kerning, metrics, size, density);
        }

        throw new InvalidOperationException($"Glyphs at {size}px × {density} don't fit in a 4096² atlas.");
    }

    /// <summary>White texels with the glyph coverage as alpha, so the draw color tints the text.</summary>
    static Texture CreateAtlasTexture(nint renderer, byte[] coverage, int dim)
    {
        var pixels = new uint[dim * dim];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = 0x00FFFFFFu | (uint)coverage[i] << 24; // ABGR8888: A in the high byte

        nint handle = SDL.SDL_CreateTexture(renderer, SDL.PIXELFORMAT_ABGR8888, SDL.TEXTUREACCESS_STATIC, dim, dim);
        if (handle == 0)
            throw new InvalidOperationException($"SDL_CreateTexture failed: {SDL.GetError()}");

        fixed (uint* p = pixels)
            SDL.SDL_UpdateTexture(handle, null, p, dim * 4);
        return new Texture(handle);
    }

    internal int GlyphIndex(char c) =>
        c - FirstChar is var i && (uint)i < CharCount ? i : Fallback - FirstChar;

    internal ref readonly EngineNative.Glyph Glyph(int index) => ref _glyphs[index];

    /// <summary>Kerning between two glyph indices, in virtual pixels.</summary>
    internal float Kerning(int left, int right) => _kerning[left * CharCount + right] / Density;

    /// <summary>Size of the text's layout box: the widest line by the number of lines.</summary>
    public Vector2 MeasureString(ReadOnlySpan<char> text)
    {
        float width = 0, x = 0;
        int lines = 1, prev = -1;
        foreach (char c in text)
        {
            if (c == '\n')
            {
                width = MathF.Max(width, x);
                x = 0;
                prev = -1;
                lines++;
                continue;
            }

            int i = GlyphIndex(c);
            if (prev >= 0)
                x += Kerning(prev, i);
            x += _glyphs[i].XAdvance / Density;
            prev = i;
        }

        return new Vector2(MathF.Max(width, x), lines * LineHeight);
    }

    internal static RectangleF Source(in EngineNative.Glyph g) => RectangleF.FromLTRB(g.X0, g.Y0, g.X1, g.Y1);

    public void Dispose() => Texture.Dispose();
}
