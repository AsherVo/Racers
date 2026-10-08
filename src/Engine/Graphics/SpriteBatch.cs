using System.Drawing;
using System.Numerics;
using Engine.Native;

namespace Engine;

/// <summary>
/// Batches textured quads into SDL_RenderGeometry calls, one call per texture run. This is the
/// game-facing drawing API; only its flush depends on SDL_Renderer, so a different backend can
/// replace it later without touching game code.
/// </summary>
/// <remarks>
/// Sprites, text and Rive artboards are all textured quads drawn in call order, so they layer in
/// any order. Each texture blends in its own alpha mode (straight or premultiplied).
/// </remarks>
public sealed unsafe class SpriteBatch
{
    const int MaxSprites = 4096;

    static readonly int[] Indices = BuildIndices();

    readonly Graphics _graphics;
    readonly SDL.Vertex[] _vertices = new SDL.Vertex[MaxSprites * 4];
    nint _texture;
    int _sprites;
    bool _active;

    /// <summary>Number of RenderGeometry calls issued in the last Begin/End pair.</summary>
    public int DrawCalls { get; private set; }

    internal SpriteBatch ( Graphics graphics ) => _graphics = graphics;

    internal void Begin ()
    {
        _active = true;
        DrawCalls = 0;
    }

    internal void End ()
    {
        Flush();
        _active = false;
    }

    public void Draw ( Texture texture, Vector2 position, Color color ) =>
        Draw( texture, position, null, color, 0f, Vector2.Zero, Vector2.One );

    public void Draw ( Texture texture, RectangleF destination, Color color ) =>
        Draw( texture, destination.Location.ToVector2(), null, color, 0f, Vector2.Zero,
            new Vector2( destination.Width / texture.Width, destination.Height / texture.Height ) );

    public void FillRectangle ( RectangleF rect, Color color ) => Draw( _graphics.Pixel, rect, color );

    /// <summary>Draws an artboard at its natural size, top-left at <paramref name="position"/>.</summary>
    public void Draw ( RiveInstance rive, Vector2 position, Color color ) =>
        Draw( rive, position, color, 0f, Vector2.Zero, Vector2.One );

    /// <summary>Stretches an artboard to fill <paramref name="destination"/>.</summary>
    public void Draw ( RiveInstance rive, RectangleF destination, Color color ) => Draw( rive.Texture, destination, color );

    /// <param name="origin">Rotation/scale pivot in artboard units.</param>
    /// <param name="scale">Relative to the artboard's natural size.</param>
    public void Draw ( RiveInstance rive, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale )
    {
        var texture = rive.Texture;
        var pixelsPerUnit = new Vector2( texture.Width / rive.Width, texture.Height / rive.Height );
        Draw( texture, position, null, color, rotation, origin * pixelsPerUnit, scale / pixelsPerUnit );
    }

    /// <summary>Draws text with its first line's top-left at <paramref name="position"/>. '\n' starts a new line.</summary>
    public void DrawString ( Font font, ReadOnlySpan< char > text, Vector2 position, Color color, float scale = 1f )
    {
        float x = 0, baseline = font.Ascent, toVirtual = 1f / font.Density;
        int prev = -1;
        foreach ( char c in text )
        {
            if ( c == '\n' )
            {
                x = 0;
                baseline += font.LineHeight;
                prev = -1;
                continue;
            }

            int i = font.GlyphIndex( c );
            if ( prev >= 0 )
                x += font.Kerning( prev, i );

            ref readonly var g = ref font.Glyph( i );
            if ( g.X1 > g.X0 )
            {
                var at = position + new Vector2( x + g.XOffset * toVirtual, baseline + g.YOffset * toVirtual ) * scale;
                Draw( font.Texture, at, Font.Source( g ), color, 0f, Vector2.Zero, new Vector2( scale * toVirtual ) );
            }

            x += g.XAdvance * toVirtual;
            prev = i;
        }
    }

    /// <param name="source">Region of the texture in pixels; null for the whole texture.</param>
    /// <param name="origin">Rotation/scale pivot in source pixels, relative to the region's top-left.</param>
    public void Draw ( Texture texture, Vector2 position, RectangleF? source, Color color, float rotation, Vector2 origin, Vector2 scale )
    {
        if ( !_active )
            throw new InvalidOperationException( "SpriteBatch.Draw called outside of Game.Draw." );

        if ( texture.Handle != _texture || _sprites == MaxSprites )
        {
            Flush();
            _texture = texture.Handle;
        }

        var src = source ?? new RectangleF( 0, 0, texture.Width, texture.Height );
        float u0 = src.Left / texture.Width, v0 = src.Top / texture.Height;
        float u1 = src.Right / texture.Width, v1 = src.Bottom / texture.Height;

        // Quad corners relative to the origin, scaled, then rotated and translated.
        float x0 = -origin.X * scale.X, y0 = -origin.Y * scale.Y;
        float x1 = ( src.Width - origin.X ) * scale.X, y1 = ( src.Height - origin.Y ) * scale.Y;
        var ( sin, cos ) = rotation == 0f ? ( 0f, 1f ) : MathF.SinCos( rotation );
        if ( texture.Premultiplied )
            color = new Color( color.R * color.A, color.G * color.A, color.B * color.A, color.A );

        var v = _vertices.AsSpan( _sprites * 4, 4 );
        v[0] = Vertex( x0, y0, u0, v0 );
        v[1] = Vertex( x1, y0, u1, v0 );
        v[2] = Vertex( x1, y1, u1, v1 );
        v[3] = Vertex( x0, y1, u0, v1 );
        _sprites++;

        SDL.Vertex Vertex ( float x, float y, float u, float tv ) => new()
        {
            X = position.X + x * cos - y * sin,
            Y = position.Y + x * sin + y * cos,
            R = color.R,
            G = color.G,
            B = color.B,
            A = color.A,
            U = u,
            V = tv,
        };
    }

    void Flush ()
    {
        if ( _sprites == 0 )
            return;

        fixed ( SDL.Vertex* vertices = _vertices )
        fixed ( int* indices = Indices )
        {
            SDL.SDL_RenderGeometry( _graphics.Renderer, _texture, vertices, _sprites * 4, indices, _sprites * 6 );
        }

        _sprites = 0;
        DrawCalls++;
    }

    static int[] BuildIndices ()
    {
        var indices = new int[MaxSprites * 6];
        for ( int i = 0, v = 0; i < indices.Length; i += 6, v += 4 )
        {
            indices[i] = v;
            indices[i + 1] = v + 1;
            indices[i + 2] = v + 2;
            indices[i + 3] = v;
            indices[i + 4] = v + 2;
            indices[i + 5] = v + 3;
        }
        return indices;
    }
}
