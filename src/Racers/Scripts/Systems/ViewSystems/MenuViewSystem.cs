using Engine;
using MintECS;

namespace Racers;

public class MenuViewSystem : WorldManipulator, IViewSystem
{
    const string BUTTON_LIST = "buttons";
    const string BUTTON_VIEW_MODEL = "MenuItem";

    const float MENU_X = 6f;
    const float MENU_TOP_MARGIN = 16f;

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

    public uint Setup ( uint canvas, int screenHeight, params string[] labels )
    {
        var menu = Get< PrefabSystem >().CreateFromData( "Menu", canvas );
        Get< Rect >( menu ).pivot = new Vector2( 0, 1 );
        Get< Position >( menu ).value = new Vector2( MENU_X, screenHeight - MENU_TOP_MARGIN );

        foreach ( var label in labels )
            AddButton( menu, label );

        return menu;
    }

    public uint AddButton ( uint menu, string label )
    {
        var button = Create( $"Menu Button { label }",
            new MenuButton(),
            new ButtonLabel { text = label }
        );

        SetRelation( button, menu, new RiveListItem { list = BUTTON_LIST, viewModel = BUTTON_VIEW_MODEL } );
        return button;
    }

    public void RemoveButton ( uint button )
        => Destroy( button );
}
