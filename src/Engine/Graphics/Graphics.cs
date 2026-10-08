using Engine.Native;

namespace Engine;

public sealed unsafe class Graphics : IDisposable
{
    internal nint renderer { get; }

    /// <summary>Resolution the game draws in, in game pixels. (0, 0) is the bottom-left corner.</summary>
    public int width { get; private set; }
    public int height { get; private set; }

    /// <summary>Output pixels per game pixel at the current window size; a density for sharp fonts and Rive.</summary>
    public float pixelScale { get; private set; } = 1f;

    /// <summary>1x1 white texture for drawing solid rectangles.</summary>
    public Texture pixel { get; }

    RiveRuntime? lazyRive;

    /// <summary>Created on first use, so games without Rive never load it.</summary>
    internal RiveRuntime rive => lazyRive ??= RiveRuntime.Create( renderer );

    /// <summary>Advances and renders Rive instances; runs between the game's Update and Draw.</summary>
    internal void UpdateRive ( float seconds ) => lazyRive?.Update( seconds );

    internal Graphics ( nint renderer )
    {
        this.renderer = renderer;
        pixel = CreateSolid( renderer );
    }

    internal void SetView ( int width, int height, float pixelScale )
    {
        this.width = width;
        this.height = height;
        this.pixelScale = pixelScale;
    }

    static Texture CreateSolid ( nint renderer )
    {
        nint handle = SDL.SDL_CreateTexture( renderer, SDL.PIXELFORMAT_ABGR8888, SDL.TEXTUREACCESS_STATIC, 1, 1 );
        if ( handle == 0 )
            throw new InvalidOperationException( $"SDL_CreateTexture failed: {SDL.GetError()}" );

        uint white = 0xFFFFFFFF;
        SDL.SDL_UpdateTexture( handle, null, &white, 4 );
        return new Texture( handle );
    }

    public void Dispose ()
    {
        lazyRive?.Dispose();
        pixel.Dispose();
    }
}
