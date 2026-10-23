using Engine.Native;

namespace Engine;

/// <summary>
/// Loads assets through SDL's file I/O, which already knows each platform's storage: the app bundle
/// on Apple platforms, APK assets on Android (relative paths), and the Emscripten FS in the browser.
/// </summary>
public sealed unsafe class ContentManager
{
    readonly Graphics? graphics;

    public ContentManager ( Graphics graphics, string root )
    {
        this.graphics = graphics;
        this.root = root;
    }

    /// <summary>
    /// Reads files only (bytes, text, YAML, listings), for tools and headless runs that have no window. Loading a
    /// texture, font or Rive file throws. SDL's file I/O works without <c>SDL_Init</c>.
    /// </summary>
    public ContentManager ( string root )
    {
        this.root = root;
    }

    public string root { get; }

    /// <summary>False for a files-only manager, which can't load textures, fonts or Rive files.</summary>
    public bool hasGraphics => graphics != null;

    public byte[] LoadBytes ( string path ) => ReadFile( root + path );

    public string LoadText ( string path ) => System.Text.Encoding.UTF8.GetString( LoadBytes( path ) );

    /// <exception cref="YamlException">The file isn't valid YAML or doesn't match <typeparamref name="T"/>; it lists every problem.</exception>
    public T LoadYaml< [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( string path ) =>
        ReadYaml< T >( root, path );

    /// <summary>Reads YAML without a renderer, for settings needed before the window opens.</summary>
    internal static T ReadYaml< [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( string root, string path ) =>
        Yaml.Deserialize< T >( System.Text.Encoding.UTF8.GetString( ReadFile( root + path ) ), source: path );

    /// <summary>Lists the files under <paramref name="directory"/> whose paths match <paramref name="pattern"/>,
    /// relative to that directory. '*' and '?' are wildcards that never match a '/'.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory doesn't exist or can't be read.</exception>
    public string[] FindFiles ( string directory, string pattern )
    {
        string full = root + directory;
        byte** matches = SDL.SDL_GlobDirectory( full, pattern, 0, out int count );
        if ( matches == null )
            throw new DirectoryNotFoundException( $"SDL_GlobDirectory failed for '{full}': {SDL.GetError()}" );

        try
        {
            var files = new string[count];
            for ( int i = 0; i < count; i++ )
                files[i] = SDL.Utf8( matches[i] ) ?? "";

            return files;
        }
        finally
        {
            SDL.SDL_free( matches );
        }
    }

    static byte[] ReadFile ( string full )
    {
        void* data = SDL.SDL_LoadFile( full, out nuint size );
        if ( data == null )
            throw new FileNotFoundException( $"SDL_LoadFile failed for '{full}': {SDL.GetError()}" );

        try
        {
            return new ReadOnlySpan< byte >( data, checked( ( int )size ) ).ToArray();
        }
        finally
        {
            SDL.SDL_free( data );
        }
    }

    /// <param name="size">Pixel height in game pixels.</param>
    /// <param name="density">Atlas pixels per game pixel. <see cref="Graphics.pixelScale"/> keeps text sharp
    /// at the window's current scale.</param>
    /// <exception cref="InvalidOperationException">This manager has no graphics.</exception>
    public Font LoadFont ( string path, float size, float density = 2f ) =>
        Font.Bake( RequireGraphics( path ).renderer, LoadBytes( path ), size, density );

    /// <exception cref="InvalidOperationException">This manager has no graphics.</exception>
    public RiveFile LoadRive ( string path ) => RequireGraphics( path ).rive.Load( LoadBytes( path ), path );

    /// <exception cref="InvalidOperationException">This manager has no graphics.</exception>
    public Texture LoadTexture ( string path, TextureFilter filter = TextureFilter.Linear )
    {
        nint renderer = RequireGraphics( path ).renderer;
        string full = root + path;
        nint surface = SDL.SDL_LoadPNG( full );
        if ( surface == 0 )
            throw new FileNotFoundException( $"SDL_LoadPNG failed for '{full}': {SDL.GetError()}" );

        try
        {
            var alpha = ReadAlpha( surface );
            nint texture = SDL.SDL_CreateTextureFromSurface( renderer, surface );
            if ( texture == 0 )
                throw new InvalidOperationException( $"SDL_CreateTextureFromSurface failed: {SDL.GetError()}" );

            return new Texture( texture, filter, alpha: alpha );
        }
        finally
        {
            SDL.SDL_DestroySurface( surface );
        }
    }

    Graphics RequireGraphics ( string path ) =>
        graphics ?? throw new InvalidOperationException( $"Can't load '{path}': this ContentManager reads files only and has no graphics." );

    /// <summary>Copies a surface's alpha, one byte per pixel with rows top-down.</summary>
    static byte[] ReadAlpha ( nint surface )
    {
        // ABGR8888 is R, G, B, A in memory on little-endian targets, which all of ours are.
        SDL.Surface* rgba = SDL.SDL_ConvertSurface( surface, SDL.PIXELFORMAT_ABGR8888 );
        if ( rgba == null )
            throw new InvalidOperationException( $"SDL_ConvertSurface failed: {SDL.GetError()}" );

        try
        {
            if ( !SDL.SDL_LockSurface( rgba ) )
                throw new InvalidOperationException( $"SDL_LockSurface failed: {SDL.GetError()}" );

            int width = rgba->w, height = rgba->h;
            var alpha = new byte[width * height];
            for ( int y = 0; y < height; y++ )
            {
                byte* row = ( byte* )rgba->pixels + y * rgba->pitch;
                for ( int x = 0; x < width; x++ )
                    alpha[y * width + x] = row[x * 4 + 3];
            }

            SDL.SDL_UnlockSurface( rgba );
            return alpha;
        }
        finally
        {
            SDL.SDL_DestroySurface( ( nint )rgba );
        }
    }

    /// <summary>Where this platform keeps the game's content: next to the executable on desktop, in the bundle or APK on mobile.</summary>
    public static string DefaultRoot () => SDL.GetPlatform() switch
    {
        // Relative paths resolve against the APK's assets folder.
        "Android" => "Content/",
        "Emscripten" => "/Content/",
        _ => Path.Combine( SDL.Utf8( SDL.SDL_GetBasePath() ) ?? AppContext.BaseDirectory, "Content" ) + Path.DirectorySeparatorChar,
    };
}
