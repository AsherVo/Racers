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

    public uint CreateFromData ( string id, uint canvas = 0 )
    {
        var data = Toolbox.I.data;

        if ( !data.Exists( id ) )
        {
            Log.Warning( $"Trying to create actor from data, but '{ id }` does not exist! Aborting." );
            return 0;
        }

        var actor = CreateActor( id, canvas );
        Add( actor, new DataId { id = id } );

        if ( data.TryGetBlock( id, "Rive", out var riveBlock ) )
        {
            AddRive( actor,
                data.GetString( riveBlock, "Path" ),
                data.GetString( riveBlock, "Artboard" )
            );
        }

        if ( data.TryGetBlock( id, "Sprite", out var spriteBlock ) )
        {
            AddSprite( actor,
                data.GetString( spriteBlock, "Path" )
            );
        }

        return actor;
    }

    public uint CreateActor ( string name, uint canvas = 0 )
    {
        var result = Create( name );
        Add< Position >( result );

        if ( canvas != 0 )
            SetRelation< OnCanvas >( result, canvas );

        return result;
    }

    public uint CreateSprite ( string name, string path, uint canvas = 0, float width = 0f, float height = 0f )
    {
        var actor = CreateActor( name, canvas );
        AddSprite( actor, path, width, height );
        return actor;
    }

    public uint CreateRive ( string name, string path, string artboard = null, uint canvas = 0, float width = 0f, float height = 0f )
    {
        var actor = CreateActor( name, canvas );
        AddRive( actor, path, artboard, width, height );
        return actor;
    }

    public uint CreateText ( string name, string text, string font, int fontSize, Color color, uint canvas = 0 )
    {
        var actor = CreateActor( name, canvas );

        var fontAsset = Toolbox.I.load.Font( font, fontSize );
        var size = fontAsset.MeasureString( text );

        AddBatch( actor, 
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
            }
        );

        return actor;
    }

    public uint CreateSolid ( string name, float width, float height, Color color, uint canvas = 0 )
    {
        var actor = CreateActor( name, canvas );

        AddBatch( actor, 
            new DrawSolid {

            },
            new DrawColor {
                color = color
            },
            new Rect {
                width = width,
                height = height
            }
        );

        return actor;
    }

    void AddSprite ( uint actor, string path, float width = 0f, float height = 0f )
    {
        var texture = Toolbox.I.load.Texture( path );
        AddBatch( actor, 
            new DrawSprite { 
                path = path 
            },
            new Rect {
                width = width != 0f ? width : texture.width,
                height = height != 0f ? height : texture.height
            }
        );
    }

    void AddRive ( uint actor, string path, string artboard = null, float width = 0f, float height = 0f )
    {
        var artboardSize = Toolbox.I.load.Rive( path ).ArtboardSize( artboard );
        AddBatch( actor, 
            new DrawRive {
                path = path,
                artboard = artboard
            },
            new Rect {
                width = width != 0f ? width : artboardSize.X,
                height = height != 0f ? height : artboardSize.Y
            }
        );
    }
}
