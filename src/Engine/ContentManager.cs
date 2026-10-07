using Engine.Native;

namespace Engine;

/// <summary>
/// Loads assets through SDL's file I/O, which already knows each platform's storage: the app bundle
/// on Apple platforms, APK assets on Android (relative paths), and the Emscripten FS in the browser.
/// </summary>
public sealed unsafe class ContentManager(nint renderer, string root)
{
    public string Root { get; } = root;

    public Texture LoadTexture(string path, TextureFilter filter = TextureFilter.Linear)
    {
        string full = Root + path;
        nint surface = SDL.SDL_LoadPNG(full);
        if (surface == 0)
            throw new FileNotFoundException($"SDL_LoadPNG failed for '{full}': {SDL.GetError()}");

        try
        {
            nint texture = SDL.SDL_CreateTextureFromSurface(renderer, surface);
            if (texture == 0)
                throw new InvalidOperationException($"SDL_CreateTextureFromSurface failed: {SDL.GetError()}");

            return new Texture(texture, filter);
        }
        finally
        {
            SDL.SDL_DestroySurface(surface);
        }
    }

    internal static string DefaultRoot() => SDL.GetPlatform() switch
    {
        // Relative paths resolve against the APK's assets folder.
        "Android" => "Content/",
        "Emscripten" => "/Content/",
        _ => Path.Combine(SDL.Utf8(SDL.SDL_GetBasePath()) ?? AppContext.BaseDirectory, "Content") + Path.DirectorySeparatorChar,
    };
}
