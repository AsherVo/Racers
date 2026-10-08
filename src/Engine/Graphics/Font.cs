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
    const int FIRST_CHAR = 32, CHAR_COUNT = 95;
    const char FALLBACK = '?';

    readonly EngineNative.Glyph[] glyphs;
    readonly float[] kerning;

    public Texture texture { get; }

    /// <summary>Pixel height the font was loaded at, in virtual-resolution pixels.</summary>
    public float size { get; }

    /// <summary>Distance from the top of a line to the baseline.</summary>
    public float ascent { get; }

    /// <summary>Distance between baselines of consecutive lines.</summary>
    public float lineHeight { get; }

    /// <summary>Atlas pixels per virtual pixel.</summary>
    internal float density { get; }

    Font ( Texture texture, EngineNative.Glyph[] glyphs, float[] kerning, EngineNative.FontMetrics metrics, float size, float density )
    {
        this.texture = texture;
        this.glyphs = glyphs;
        this.kerning = kerning;
        this.size = size;
        this.density = density;
        ascent = metrics.ascent / density;
        lineHeight = ( metrics.ascent - metrics.descent + metrics.lineGap ) / density;
    }

    internal static Font Bake ( nint renderer, ReadOnlySpan< byte > ttf, float size, float density )
    {
        var glyphs = new EngineNative.Glyph[CHAR_COUNT];
        var kerning = new float[CHAR_COUNT * CHAR_COUNT];
        EngineNative.FontMetrics metrics;

        for ( int dim = 256; dim <= 4096; dim *= 2 )
        {
            var atlas = new byte[dim * dim];
            int result;
            fixed ( byte* ttfPtr = ttf )
            fixed ( byte* atlasPtr = atlas )
            fixed ( EngineNative.Glyph* glyphPtr = glyphs )
            fixed ( float* kerningPtr = kerning )
            {
                result = EngineNative.en_font_bake( ttfPtr, ttf.Length, size * density, FIRST_CHAR, CHAR_COUNT,
                    atlasPtr, dim, dim, glyphPtr, kerningPtr, &metrics );
            }

            if ( result < 0 )
                throw new InvalidDataException( "Not a readable TrueType font." );
            if ( result > 0 )
                return new Font( CreateAtlasTexture( renderer, atlas, dim ), glyphs, kerning, metrics, size, density );
        }

        throw new InvalidOperationException( $"Glyphs at {size}px × {density} don't fit in a 4096² atlas." );
    }

    /// <summary>White texels with the glyph coverage as alpha, so the draw color tints the text.</summary>
    static Texture CreateAtlasTexture ( nint renderer, byte[] coverage, int dim )
    {
        var pixels = new uint[dim * dim];
        for ( int i = 0; i < pixels.Length; i++ )
            pixels[i] = 0x00FFFFFFu | ( uint )coverage[i] << 24; // ABGR8888: A in the high byte

        nint handle = SDL.SDL_CreateTexture( renderer, SDL.PIXELFORMAT_ABGR8888, SDL.TEXTUREACCESS_STATIC, dim, dim );
        if ( handle == 0 )
            throw new InvalidOperationException( $"SDL_CreateTexture failed: {SDL.GetError()}" );

        fixed ( uint* p = pixels )
            SDL.SDL_UpdateTexture( handle, null, p, dim * 4 );
        return new Texture( handle );
    }

    internal int GlyphIndex ( char c ) =>
        c - FIRST_CHAR is var i && ( uint )i < CHAR_COUNT ? i : FALLBACK - FIRST_CHAR;

    internal ref readonly EngineNative.Glyph Glyph ( int index ) => ref glyphs[index];

    /// <summary>Kerning between two glyph indices, in virtual pixels.</summary>
    internal float Kerning ( int left, int right ) => kerning[left * CHAR_COUNT + right] / density;

    /// <summary>Size of the text's layout box: the widest line by the number of lines.</summary>
    public Vector2 MeasureString ( ReadOnlySpan< char > text )
    {
        float width = 0, x = 0;
        int lines = 1, prev = -1;
        foreach ( char c in text )
        {
            if ( c == '\n' )
            {
                width = MathF.Max( width, x );
                x = 0;
                prev = -1;
                lines++;
                continue;
            }

            int i = GlyphIndex( c );
            if ( prev >= 0 )
                x += Kerning( prev, i );
            x += glyphs[i].xAdvance / density;
            prev = i;
        }

        return new Vector2( MathF.Max( width, x ), lines * lineHeight );
    }

    internal static RectangleF Source ( in EngineNative.Glyph g ) => RectangleF.FromLTRB( g.x0, g.y0, g.x1, g.y1 );

    public void Dispose () => texture.Dispose();
}
