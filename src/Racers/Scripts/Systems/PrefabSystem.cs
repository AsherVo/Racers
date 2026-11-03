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

    public uint CreateFromData ( string id, string name = null, uint canvas = 0 )
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

    public void AddFromData ( uint entity, string id, uint canvas = 0 )
    {
        var data = Toolbox.I.data;

        if ( !data.Exists( id ) )
        {
            Log.Warning( $"Trying to add data to { GetName( entity ) }, but '{ id }` does not exist! Aborting." );
            return;
        }

        if ( world.phase == WorldPhase.View )
            Add( entity, new ViewDataId { id = id } );
        else
            AddSimComponents( entity, id );

        // VIEW COMPONENTS
        if ( canvas != 0 )
            SetRelation< OnCanvas >( entity, canvas );

        if ( data.TryGetBlock( id, "Display", out var displayBlock ) )
        {
            var name = data.GetString( displayBlock, "Name" );
            var description = data.GetString( displayBlock, "Description" );
            var iconPath = data.GetString( displayBlock, "Icon" );

            if ( !string.IsNullOrEmpty( name ) )
                Add( entity, new DisplayName { value = name } );

            if ( !string.IsNullOrEmpty( description ) )
                Add( entity, new DisplayDescription { value = description } );

            if ( !string.IsNullOrEmpty( iconPath ) )
                Add( entity, new DisplayIcon { path = iconPath } );
        }

        if ( data.TryGetBlock( id, "Rive", out var riveBlock ) )
        {
            AddRive( entity,
                data.GetString( riveBlock, "Path" ),
                data.GetString( riveBlock, "Artboard" ),
                layout: data.GetBool( riveBlock, "Layout" )
            );
        }

        if ( data.TryGetBlock( id, "Sprite", out var spriteBlock ) )
        {
            AddSprite( entity,
                data.GetString( spriteBlock, "Path" )
            );

            var colorHex = data.GetString( spriteBlock, "Color" );
            if ( colorHex != null )
                Add( entity, new DrawColor { value = Utils.HexToColor( colorHex ) } );

            if ( data.GetBool( spriteBlock, "Animated" ) )
            {
                Add( entity, new SpriteAnimation {
                    animation = Loader.GetAnimationPrefix( data.GetString( spriteBlock, "Path" ) ),
                    fps = data.GetInt( spriteBlock, "FPS" )
                });
            }
        }

        var group = data.GetGroup( id );
        switch ( group )
        {
            case Group.Misc:
                Add( entity, new Position() );
                break;

            case Group.Buttons:
                Add( entity, new Interactable() );
                Add( entity, new ClickTarget() );
                Add( entity, new HoverTarget() );
                Add( entity, new Position() );
                break;

            case Group.Screens:
                Add( entity, new Interactable() );
                Add( entity, new RiveInput() );
                Add( entity, new Position() );
                break;
        }
    }

    void AddSimComponents ( uint entity, string id )
    {
        var data = Toolbox.I.data;

        Add( entity, new DataId { id = id } );

        if ( data.TryGetBlock( id, "Unit", out var unitBlock ) )
        {
            if ( data.GetBool( unitBlock, "IsUnit" ) )
                Add( entity, new Unit {} );
        }

        if ( data.TryGetBlock( id, "Stats", out var statsBlock ) )
        {
            var resources = Get< ResourceSystem >();

            var health = data.GetInt( statsBlock, "Health" );
            if ( health > 0 )
                resources.Add( entity, "Health", startValue: health );
        }
    }

    public uint CreateActor ( string name, uint canvas = 0 )
    {
        var result = Create( name );
        Add( result, new Position() );

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

    void AddRive ( uint actor, string path, string artboard = null, float width = 0f, float height = 0f, bool layout = false )
    {
        AddBatch( actor,
            new DrawRive {
                path = path,
                artboard = artboard,
                layout = layout
            },
            new Rect {
                width = width,
                height = height
            }
        );
    }
}
