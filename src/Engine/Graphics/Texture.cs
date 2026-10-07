using Engine.Native;

namespace Engine;

public enum TextureFilter { Linear, PixelArt }

public sealed class Texture : IDisposable
{
    internal nint Handle { get; private set; }

    public int Width { get; }
    public int Height { get; }

    internal Texture(nint handle, TextureFilter filter = TextureFilter.Linear)
    {
        Handle = handle;
        SDL.SDL_GetTextureSize(handle, out float w, out float h);
        Width = (int)w;
        Height = (int)h;
        SDL.SDL_SetTextureScaleMode(handle, filter == TextureFilter.PixelArt ? SDL.SCALEMODE_PIXELART : SDL.SCALEMODE_LINEAR);
    }

    public void Dispose()
    {
        if (Handle == 0)
            return;

        SDL.SDL_DestroyTexture(Handle);
        Handle = 0;
    }
}
