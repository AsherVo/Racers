using RectangleF = System.Drawing.RectangleF;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public class Loader
{
    const string SPRITE_PATH = "sprites/";
    const string SPRITE_SUFFIX = ".png";

    const string RIVES_PATH = "rives/";
    const string RIVE_SUFFIX = ".riv";

    const string FONTS_PATH = "fonts/";
    const string FONT_SUFFIX = ".ttf";

    Dictionary< string, Texture > textures;
    Dictionary< string, RiveFile > rives;
    Dictionary< string, Dictionary< int, Font > > fonts;
    Dictionary< string, string[] > spritePaths;

    public Loader ()
        => Setup();

    public void Setup ()
    {
        textures = new();
        rives = new();
        fonts = new();
        spritePaths = new();
    }

    public void Dispose ()
    {
        foreach ( var value in textures.Values )
            value.Dispose();

        foreach ( var value in rives.Values )
            value.Dispose();

        foreach ( var sizeValue in fonts.Values )
        {
            foreach ( var fontValue in sizeValue.Values )
                fontValue.Dispose();
        }

        textures.Clear();
        rives.Clear();
        fonts.Clear();
        spritePaths.Clear();
    }

    public Font Font ( string path, int fontSize )
    {
        if ( !fonts.ContainsKey( path ) )
            fonts[ path ] = new();
        
        if ( !fonts[ path ].ContainsKey( fontSize ) )
        {
            var game = Toolbox.I.game;
            var usePath = $"{ FONTS_PATH }/{ path }{ FONT_SUFFIX }";
            fonts[ path ][ fontSize ] = game.content.LoadFont( usePath, fontSize, game.graphics.pixelScale );
        }

        return fonts[ path ][ fontSize ];
    }

    public RiveFile Rive ( string path )
    {
        if ( !rives.ContainsKey( path ) )
        {
            var usePath = $"{ RIVES_PATH }/{ path }{ RIVE_SUFFIX }";
            rives[ path ] = Toolbox.I.game.content.LoadRive( usePath );
        }

        return rives[ path ];
    }

    public Texture Texture ( string path )
    {
        if ( !textures.ContainsKey( path ) )
        {
            var usePath = $"{ SPRITE_PATH }/{ path }{ SPRITE_SUFFIX }";
            textures[ path ] = Toolbox.I.game.content.LoadTexture( usePath, TextureFilter.PixelArt );
        }

        return textures[ path ];
    }

    public static string GetAnimationPrefix ( string path )
    {
        var prefix = path.TrimEnd( "0123456789".ToCharArray() );
        return prefix.EndsWith( '_' ) ? prefix[ ..^1 ] : prefix;
    }

    public string[] GetSpritePaths ( string prefix )
    {
        if ( !spritePaths.ContainsKey( prefix ) )
            spritePaths[ prefix ] = FindSpritePaths( prefix );

        return spritePaths[ prefix ];
    }

    string[] FindSpritePaths ( string prefix )
    {
        var slash = prefix.LastIndexOf( '/' );
        var directory = SPRITE_PATH + prefix[ ..( slash + 1 ) ];
        var name = prefix[ ( slash + 1 ).. ];

        var frames = new List< ( string suffix, string number, int value ) >();
        foreach ( var file in Toolbox.I.game.content.FindFiles( directory, $"{ name }*{ SPRITE_SUFFIX }" ) )
        {
            var suffix = file[ name.Length..^SPRITE_SUFFIX.Length ];
            var number = suffix.StartsWith( '_' ) ? suffix[ 1.. ] : suffix;
            if ( number.Length > 0 && number.All( char.IsAsciiDigit ) && int.TryParse( number, out var value ) )
                frames.Add( ( suffix, number, value ) );
        }

        return frames
            .OrderBy( frame => frame.value )
            .ThenBy( frame => frame.suffix.Length )
            .Select( frame => prefix + frame.suffix )
            .ToArray();
    }
}
