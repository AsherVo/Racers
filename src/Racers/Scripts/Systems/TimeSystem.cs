using Engine;
using MintECS;

namespace Racers;

public class TimeSystem : WorldManipulator, ISystem
{
    public void StartSystem ( World world )
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {
        this.world = null;
    }

    public void TickSystem ( World world )
    {
        
    }

    public void Update ( GameTime time )
    {
        world.Send( new TimePassed { dt = time.dt } );
    }
}
