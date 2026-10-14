using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public class Loader
{
    Dictionary< string, Texture > textures;
    Dictionary< string, RiveFile > rives;
    Dictionary< string, Dictionary< int, Font > > fonts;

    public Loader ()
        => Setup();

    public void Setup ()
    {
        textures = new();
        rives = new();
        fonts = new();
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
    }

    public Font Font ( string path, int fontSize )
    {
        if ( !fonts.ContainsKey( path ) )
            fonts[ path ] = new();
        
        if ( !fonts[ path ].ContainsKey( fontSize ) )
        {
            var game = Toolbox.I.game;
            fonts[ path ][ fontSize ] = game.content.LoadFont( path, fontSize, game.graphics.pixelScale );
        }

        return fonts[ path ][ fontSize ];
    }

    public RiveFile Rive ( string path )
    {
        if ( !rives.ContainsKey( path ) )
            rives[ path ] = Toolbox.I.game.content.LoadRive( path );

        return rives[ path ];
    }

    public Texture Texture ( string path )
    {
        if ( !textures.ContainsKey( path ) )
            textures[ path ] = Toolbox.I.game.content.LoadTexture( path, TextureFilter.PixelArt );

        return textures[ path ];
    }
}
