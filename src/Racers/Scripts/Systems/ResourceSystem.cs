using Engine;
using MintECS;

namespace Racers;

public class ResourceSystem : WorldManipulator, ISimSystem
{
    public void StartSystem ( World world )
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {

    }

    public void FixedUpdate ( World world )
    {

    }
}
