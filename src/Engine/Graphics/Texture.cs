using Engine.Native;

namespace Engine;

public enum TextureFilter { Linear, PixelArt }

public sealed class Texture : IDisposable
{
    internal nint handle { get; private set; }

    public int width { get; }
    public int height { get; }

    /// <summary>
    /// Color is premultiplied by alpha (Rive output). Each texture blends in its own mode, so
    /// straight and premultiplied textures can be drawn in any order.
    /// </summary>
    public bool premultiplied { get; }

    /// <summary>One byte of alpha per pixel, rows top-down; null when the pixels only live on the GPU.</summary>
    readonly byte[]? alpha;

    /// <summary>True if the texture keeps a CPU copy of its alpha for <see cref="AlphaAt"/>. Textures loaded from PNGs do.</summary>
    public bool hasAlpha => alpha != null;

    internal Texture ( nint handle, TextureFilter filter = TextureFilter.Linear, bool premultiplied = false, byte[]? alpha = null )
    {
        this.handle = handle;
        this.premultiplied = premultiplied;
        this.alpha = alpha;
        SDL.SDL_GetTextureSize( handle, out float w, out float h );
        width = ( int )w;
        height = ( int )h;
        SDL.SDL_SetTextureScaleMode( handle, filter == TextureFilter.PixelArt ? SDL.SCALEMODE_PIXELART : SDL.SCALEMODE_LINEAR );
        SDL.SDL_SetTextureBlendMode( handle, premultiplied ? SDL.BLENDMODE_BLEND_PREMULTIPLIED : SDL.BLENDMODE_BLEND );

        if ( alpha != null && alpha.Length != width * height )
            throw new ArgumentException( $"Alpha has {alpha.Length} values for a {width}x{height} texture.", nameof( alpha ) );
    }

    /// <summary>The alpha (0 to 1) of a pixel, counted from the top-left. Pixels outside the texture are transparent.</summary>
    /// <exception cref="InvalidOperationException">The texture has no CPU copy of its alpha (see <see cref="hasAlpha"/>).</exception>
    public float AlphaAt ( int x, int y )
    {
        if ( alpha == null )
            throw new InvalidOperationException( "This texture has no CPU copy of its alpha." );

        if ( x < 0 || y < 0 || x >= width || y >= height )
            return 0f;

        return alpha[y * width + x] / 255f;
    }

    public void Dispose ()
    {
        if ( handle == 0 )
            return;

        SDL.SDL_DestroyTexture( handle );
        handle = 0;
    }
}
