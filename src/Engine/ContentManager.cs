using Engine.Native;

namespace Engine;

/// <summary>
/// Loads assets through SDL's file I/O, which already knows each platform's storage: the app bundle
/// on Apple platforms, APK assets on Android (relative paths), and the Emscripten FS in the browser.
/// </summary>
public sealed unsafe class ContentManager ( Graphics graphics, string root )
{
    readonly nint renderer = graphics.Renderer;

    public string Root { get; } = root;

    public byte[] LoadBytes ( string path )
    {
        string full = Root + path;
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

    public string LoadText ( string path ) => System.Text.Encoding.UTF8.GetString( LoadBytes( path ) );

    /// <exception cref="YamlException">The file isn't valid YAML or doesn't match <typeparamref name="T"/>; it lists every problem.</exception>
    public T LoadYaml< [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( string path ) =>
        Yaml.Deserialize< T >( LoadText( path ), source: path );

    /// <param name="size">Pixel height in virtual-resolution pixels.</param>
    /// <param name="density">Atlas pixels per virtual pixel; 2 keeps text sharp on Retina and when
    /// the window is larger than the virtual resolution.</param>
    public Font LoadFont ( string path, float size, float density = 2f ) =>
        Font.Bake( renderer, LoadBytes( path ), size, density );

    public RiveFile LoadRive ( string path ) => graphics.Rive.Load( LoadBytes( path ), path );

    public Texture LoadTexture ( string path, TextureFilter filter = TextureFilter.Linear )
    {
        string full = Root + path;
        nint surface = SDL.SDL_LoadPNG( full );
        if ( surface == 0 )
            throw new FileNotFoundException( $"SDL_LoadPNG failed for '{full}': {SDL.GetError()}" );

        try
        {
            nint texture = SDL.SDL_CreateTextureFromSurface( renderer, surface );
            if ( texture == 0 )
                throw new InvalidOperationException( $"SDL_CreateTextureFromSurface failed: {SDL.GetError()}" );

            return new Texture( texture, filter );
        }
        finally
        {
            SDL.SDL_DestroySurface( surface );
        }
    }

    internal static string DefaultRoot () => SDL.GetPlatform() switch
    {
        // Relative paths resolve against the APK's assets folder.
        "Android" => "Content/",
        "Emscripten" => "/Content/",
        _ => Path.Combine( SDL.Utf8( SDL.SDL_GetBasePath() ) ?? AppContext.BaseDirectory, "Content" ) + Path.DirectorySeparatorChar,
    };
}
