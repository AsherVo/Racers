using MintECS;

namespace Racers;

public static class SimSystems
{
    public static void Start ( World world )
    {
        world.Start( new PrefabSystem() );
        world.Start( new GridSystem() );
        world.Start( new RaceSystem() );
    }
}
