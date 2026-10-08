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
/// <para>
/// Positions are in game pixels with (0, 0) at the bottom-left and y pointing up; rotations are
/// counter-clockwise. Images stay upright. Sources and origins are in texture pixels from the
/// image's top-left, as an image editor shows them.
/// </para>
/// </remarks>
public sealed unsafe class SpriteBatch
{
    const int MAX_SPRITES = 4096;

    static readonly int[] Indices = BuildIndices();

    readonly Graphics graphics;
    readonly SDL.Vertex[] vertices = new SDL.Vertex[MAX_SPRITES * 4];
    nint texture;
    int sprites;
    bool active;

    /// <summary>Number of RenderGeometry calls issued in the last Begin/End pair.</summary>
    public int drawCalls { get; private set; }

    internal SpriteBatch ( Graphics graphics ) => this.graphics = graphics;

    internal void Begin ()
    {
        active = true;
        drawCalls = 0;
    }

    internal void End ()
    {
        Flush();
        active = false;
    }

    /// <summary>Draws a texture at its natural size, bottom-left at <paramref name="position"/>.</summary>
    public void Draw ( Texture texture, Vector2 position, Color color ) =>
        Draw( texture, position, null, color, 0f, new Vector2( 0f, texture.height ), Vector2.One );

    /// <summary>Stretches a texture to fill <paramref name="destination"/>, whose X and Y are its bottom-left corner.</summary>
    public void Draw ( Texture texture, RectangleF destination, Color color ) =>
        Draw( texture, destination.Location.ToVector2(), null, color, 0f, new Vector2( 0f, texture.height ),
            new Vector2( destination.Width / texture.width, destination.Height / texture.height ) );

    /// <param name="rect">X and Y are the bottom-left corner.</param>
    public void FillRectangle ( RectangleF rect, Color color ) => Draw( graphics.pixel, rect, color );

    /// <summary>Draws an artboard at its natural size, bottom-left at <paramref name="position"/>.</summary>
    public void Draw ( RiveInstance rive, Vector2 position, Color color ) =>
        Draw( rive, position, color, 0f, new Vector2( 0f, rive.height ), Vector2.One );

    /// <summary>Stretches an artboard to fill <paramref name="destination"/>, whose X and Y are its bottom-left corner.</summary>
    public void Draw ( RiveInstance rive, RectangleF destination, Color color ) => Draw( rive.texture, destination, color );

    /// <param name="origin">Rotation/scale pivot in artboard units from its top-left; it lands on <paramref name="position"/>.</param>
    /// <param name="scale">Relative to the artboard's natural size.</param>
    public void Draw ( RiveInstance rive, Vector2 position, Color color, float rotation, Vector2 origin, Vector2 scale )
    {
        var texture = rive.texture;
        var pixelsPerUnit = new Vector2( texture.width / rive.width, texture.height / rive.height );
        Draw( texture, position, null, color, rotation, origin * pixelsPerUnit, scale / pixelsPerUnit );
    }

    /// <summary>Draws text with its first line's top-left at <paramref name="position"/>. '\n' starts a new line below.</summary>
    public void DrawString ( Font font, ReadOnlySpan< char > text, Vector2 position, Color color, float scale = 1f )
    {
        float x = 0, baseline = font.ascent, toVirtual = 1f / font.density;
        int prev = -1;
        foreach ( char c in text )
        {
            if ( c == '\n' )
            {
                x = 0;
                baseline += font.lineHeight;
                prev = -1;
                continue;
            }

            int i = font.GlyphIndex( c );
            if ( prev >= 0 )
                x += font.Kerning( prev, i );

            ref readonly var g = ref font.Glyph( i );
            if ( g.x1 > g.x0 )
            {
                var at = position + new Vector2( x + g.xOffset * toVirtual, -( baseline + g.yOffset * toVirtual ) ) * scale;
                Draw( font.texture, at, Font.Source( g ), color, 0f, Vector2.Zero, new Vector2( scale * toVirtual ) );
            }

            x += g.xAdvance * toVirtual;
            prev = i;
        }
    }

    /// <param name="source">Region of the texture in pixels; null for the whole texture.</param>
    /// <param name="origin">Rotation/scale pivot in source pixels from the region's top-left; it lands on <paramref name="position"/>.</param>
    /// <param name="rotation">Counter-clockwise, in radians.</param>
    public void Draw ( Texture texture, Vector2 position, RectangleF? source, Color color, float rotation, Vector2 origin, Vector2 scale )
    {
        if ( !active )
            throw new InvalidOperationException( "SpriteBatch.Draw called outside of Game.Draw." );

        if ( texture.handle != this.texture || sprites == MAX_SPRITES )
        {
            Flush();
            this.texture = texture.handle;
        }

        var src = source ?? new RectangleF( 0, 0, texture.width, texture.height );
        float u0 = src.Left / texture.width, v0 = src.Top / texture.height;
        float u1 = src.Right / texture.width, v1 = src.Bottom / texture.height;

        // Quad corners relative to the origin, scaled, turned y-up (texture rows run down), then rotated and translated.
        float x0 = -origin.X * scale.X, y0 = origin.Y * scale.Y;
        float x1 = ( src.Width - origin.X ) * scale.X, y1 = ( origin.Y - src.Height ) * scale.Y;
        var ( sin, cos ) = rotation == 0f ? ( 0f, 1f ) : MathF.SinCos( rotation );
        if ( texture.premultiplied )
            color = new Color( color.r * color.a, color.g * color.a, color.b * color.a, color.a );

        float height = graphics.height;
        var v = vertices.AsSpan( sprites * 4, 4 );
        v[0] = Vertex( x0, y0, u0, v0 );
        v[1] = Vertex( x1, y0, u1, v0 );
        v[2] = Vertex( x1, y1, u1, v1 );
        v[3] = Vertex( x0, y1, u0, v1 );
        sprites++;

        // SDL's y runs down from the top, so the game's y is flipped on the way out.
        SDL.Vertex Vertex ( float x, float y, float u, float tv ) => new()
        {
            x = position.X + x * cos - y * sin,
            y = height - ( position.Y + x * sin + y * cos ),
            r = color.r,
            g = color.g,
            b = color.b,
            a = color.a,
            u = u,
            v = tv,
        };
    }

    void Flush ()
    {
        if ( sprites == 0 )
            return;

        fixed ( SDL.Vertex* vertices = this.vertices )
        fixed ( int* indices = Indices )
        {
            SDL.SDL_RenderGeometry( graphics.renderer, texture, vertices, sprites * 4, indices, sprites * 6 );
        }

        sprites = 0;
        drawCalls++;
    }

    static int[] BuildIndices ()
    {
        var indices = new int[MAX_SPRITES * 6];
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
