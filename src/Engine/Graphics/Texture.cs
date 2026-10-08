using Engine.Native;

namespace Engine;

public enum TextureFilter { Linear, PixelArt }

public sealed class Texture : IDisposable
{
    internal nint Handle { get; private set; }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// Color is premultiplied by alpha (Rive output). Each texture blends in its own mode, so
    /// straight and premultiplied textures can be drawn in any order.
    /// </summary>
    public bool Premultiplied { get; }

    internal Texture ( nint handle, TextureFilter filter = TextureFilter.Linear, bool premultiplied = false )
    {
        Handle = handle;
        Premultiplied = premultiplied;
        SDL.SDL_GetTextureSize( handle, out float w, out float h );
        Width = ( int )w;
        Height = ( int )h;
        SDL.SDL_SetTextureScaleMode( handle, filter == TextureFilter.PixelArt ? SDL.SCALEMODE_PIXELART : SDL.SCALEMODE_LINEAR );
        SDL.SDL_SetTextureBlendMode( handle, premultiplied ? SDL.BLENDMODE_BLEND_PREMULTIPLIED : SDL.BLENDMODE_BLEND );
    }

    public void Dispose ()
    {
        if ( Handle == 0 )
            return;

        SDL.SDL_DestroyTexture( Handle );
        Handle = 0;
    }
}
