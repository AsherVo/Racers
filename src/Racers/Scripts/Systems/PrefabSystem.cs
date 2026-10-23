using RectangleF = System.Drawing.RectangleF;
using System.Runtime.InteropServices;
using Engine;
using MintECS;
using Vo;

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

    public uint CreateFromData ( string id, uint canvas = 0, string name = null )
    {
        var data = Toolbox.I.data;

        if ( !data.Exists( id ) )
        {
            Log.Warning( $"Trying to create actor from data, but '{ id }` does not exist! Aborting." );
            return 0;
        }

        var actor = Create( name == null ? id : name );
        AddFromData( actor, id, canvas );
        return actor;
    }

    public void AddFromData ( uint actor, string id, uint canvas = 0 )
    {
        var data = Toolbox.I.data;

        if ( !data.Exists( id ) )
        {
            Log.Warning( $"Trying to add data to { GetName( actor ) }, but '{ id }` does not exist! Aborting." );
            return;
        }

        AddActor( actor, canvas );
        Add( actor, new DataId { id = id } );

        if ( data.TryGetBlock( id, "Unit", out var unitBlock ) )
        {
            if ( data.GetBool( unitBlock, "IsUnit" ) )
                Add( actor, new Unit {} );
        }

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

            var colorHex = data.GetString( spriteBlock, "Color" );
            if ( colorHex != null )
                Add( actor, new DrawColor { value = Utils.HexToColor( colorHex ) } );

            if ( data.GetBool( spriteBlock, "Animated" ) )
            {
                Add( actor, new SpriteAnimation {
                    animation = Loader.GetAnimationPrefix( data.GetString( spriteBlock, "Path" ) ),
                    fps = data.GetInt( spriteBlock, "FPS" )
                });
            }
        }

        var group = data.GetGroup( id );
        switch ( group )
        {
            case Group.Buttons:
                Add( actor, new Interactable() );
                Add( actor, new ClickTarget() );
                Add( actor, new HoverTarget() );
                break;
        }
    }

    public uint CreateActor ( string name, uint canvas = 0 )
    {
        var result = Create( name );
        AddActor( result, canvas );
        return result;
    }

    void AddActor ( uint actor, uint canvas )
    {
        Add( actor, new Position() );

        if ( canvas != 0 )
            SetRelation< OnCanvas >( actor, canvas );
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
                value = color
            },
            new Rect {
                width = size.x,
                height = size.y
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
                value = color
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
        AddBatch( actor, 
            new DrawSprite { 
                path = path 
            },
            new Rect {
                width = width,
                height = height
            }
        );
    }

    void AddRive ( uint actor, string path, string artboard = null, float width = 0f, float height = 0f )
    {
        AddBatch( actor, 
            new DrawRive {
                path = path,
                artboard = artboard
            },
            new Rect {
                width = width,
                height = height
            }
        );
    }
}
