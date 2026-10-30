using Engine;
using MintECS;

namespace Racers;

public class MenuViewSystem : WorldManipulator, IViewSystem
{
    const float MENU_X = 6f;
    const float MENU_TOP_MARGIN = 16f;
    const float BUTTON_GAP = 4f;

    public void StartSystem ( World world )
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        foreach ( var click in Read< Click >() )
        {
            if ( Exists( click.target ) && Has< MenuButton >( click.target ) )
                Log.Info( $"Menu: clicked { Get< ButtonLabel >( click.target )?.text }" );
        }
    }

    public List< uint > Setup ( uint canvas, int screenHeight, params string[] labels )
    {
        var prefab = Get< PrefabSystem >();
        var top = screenHeight - MENU_TOP_MARGIN;
        var buttons = new List< uint >();

        foreach ( var label in labels )
        {
            var button = prefab.CreateFromData( "MenuButton", canvas, $"Menu Button { label }" );
            Add( button, new MenuButton() );
            Add( button, new ButtonLabel { text = label } );

            var rect = Get< Rect >( button );
            rect.pivot = new Vector2( 0, 1 );
            Get< Position >( button ).value = new Vector2( MENU_X, top );
            top -= rect.height + BUTTON_GAP;

            buttons.Add( button );
        }

        return buttons;
    }
}
