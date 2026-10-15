using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public class PrefabSystem : WorldManipulator, ISystem
{
    public void StartSystem ( World world )
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {
        this.world = null;    
    }

    public void TickSystem ( World world ) {}

    public uint CreateSprite ( string name, string path, uint canvas = 0, float width = 0f, float height = 0f )
    {
        var texture = Toolbox.I.load.Texture( path );
        var result = Create( name,
            new DrawSprite { 
                path = path 
            },
            new Rect {
                width = width != 0f ? width : texture.width,
                height = height != 0f ? height : texture.height
            },
            new Position {}
        );

        if ( canvas != 0 )
            SetRelation< OnCanvas >( result, canvas );

        return result;
    }

    public uint CreateRive ( string name, string path, string artboard = null, uint canvas = 0, float width = 0f, float height = 0f )
    {
        var artboardSize = Toolbox.I.load.Rive( path ).ArtboardSize( artboard );
        var result = Create( name,
            new DrawRive {
                path = path,
                artboard = artboard
            },
            new Rect {
                width = width != 0f ? width : artboardSize.X,
                height = height != 0f ? height : artboardSize.Y
            },
            new Position {}
        );

        if ( canvas != 0 )
            SetRelation< OnCanvas >( result, canvas );

        return result;
    }

    public uint CreateText ( string name, string text, string font, int fontSize, Color color, uint canvas = 0 )
    {
        var size = Toolbox.I.load.Font( font, fontSize ).MeasureString( text );
        var result = Create( name,
            new DrawText {
                text = text,
                font = font,
                fontSize = fontSize
            },
            new DrawColor {
                color = color
            },
            new Rect {
                width = size.X,
                height = size.Y
            },
            new Position {}
        );

        if ( canvas != 0 )
            SetRelation< OnCanvas >( result, canvas );

        return result;
    }

    public uint CreateSolid ( string name, float width, float height, Color color, uint canvas = 0 )
    {
        var result = Create( name,
            new DrawSolid {},
            new DrawColor {
                color = color
            },
            new Rect {
                width = width,
                height = height
            },
            new Position {}
        );

        if ( canvas != 0 )
            SetRelation< OnCanvas >( result, canvas );

        return result;
    }
}
