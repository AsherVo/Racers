using Engine;
using MintECS;

namespace Racers;

public class ButtonSystem : WorldManipulator, IViewSystem
{
    const string RIVE_IDLE_TRIGGER = "idle";
    const string RIVE_HOVER_TRIGGER = "highlight";

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
        var startHovers = Read< StartHover >();
        foreach ( var startHover in startHovers )
        {
            var target = startHover.target;
            if ( Has< DrawRive >( target ) )
            {
                var rive = world.Get< RenderSystem >().GetRive( target );
                rive?.FireViewModelTrigger( RIVE_HOVER_TRIGGER );
            }
        }

        var stopHovers = Read< StopHover >();
        foreach ( var stopHover in stopHovers )
        {
            var target = stopHover.target;
            if ( Exists( target ) && Has< DrawRive >( target ) )
            {
                var rive = world.Get< RenderSystem >().GetRive( target );
                rive?.FireViewModelTrigger( RIVE_IDLE_TRIGGER );
            }
        }

        var clicks = Read< Click >();
        foreach ( var click in clicks )
        {
            if ( Exists( click.target ) && !World.IsViewEntity( click.target ) )
                Send( new PressButton { button = click.target } );
        }
    }
}
