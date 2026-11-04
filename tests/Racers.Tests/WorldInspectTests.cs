using Engine;
using MintECS;

namespace Racers.Tests;

public class WorldInspectTests
{
    abstract class TestSystem : ISystem
    {
        public void StartSystem ( World world ) { }
        public void StopSystem ( World world ) { }
    }

    abstract class TestSimSystem : TestSystem, ISimSystem
    {
        public void FixedUpdate ( World world ) { }
    }

    abstract class TestViewSystem : TestSystem, IViewSystem
    {
        public void Update ( World world, FrameTime time ) { }
    }

    sealed class FirstSim : TestSimSystem;
    sealed class SecondSim : TestSimSystem;
    sealed class Utility : TestSystem;
    sealed class FirstView : TestViewSystem;

    sealed class ViewAndDraw : TestViewSystem, IDrawSystem
    {
        public void DrawSystem ( World world, SpriteBatch batch ) { }
    }

    sealed class DrawOnly : TestSystem, IDrawSystem
    {
        public void DrawSystem ( World world, SpriteBatch batch ) { }
    }

    sealed class LastView : TestViewSystem;

    static string[] Names ( InspectorNode phase )
        => phase.children!.Select( system => system.label ).ToArray();

    [Fact]
    public void SystemsAreListedByPhaseInTheOrderTheyRun ()
    {
        var world = new World( 60 );
        world.Start( new FirstView() );
        world.Start( new SecondSim() );
        world.Start( new Utility() );
        world.Start( new DrawOnly() );
        world.Start( new ViewAndDraw() );
        world.Start( new FirstSim() );
        world.Start( new LastView() );

        var phases = world.Inspect( InspectorQuery.Nothing ).systems!;

        Assert.Equal( ["Sim", "View"], phases.Select( phase => phase.label ) );
        Assert.Equal( [nameof( SecondSim ), nameof( FirstSim )], Names( phases[0] ) );
        Assert.Equal( [nameof( FirstView ), nameof( ViewAndDraw ), nameof( LastView ), nameof( DrawOnly )], Names( phases[1] ) );
    }

    [Fact]
    public void AStoppedSystemLeavesTheList ()
    {
        var world = new World( 60 );
        var stopped = new FirstSim();
        world.Start( stopped );
        world.Start( new SecondSim() );
        world.Stop( stopped );

        var phases = world.Inspect( InspectorQuery.Nothing ).systems!;

        Assert.Equal( [nameof( SecondSim )], Names( phases[0] ) );
        Assert.Empty( phases[1].children! );
    }

    [Fact]
    public void SameSystemStartedTwiceHasDistinctKeys ()
    {
        var world = new World( 60 );
        world.Start( new FirstSim() );
        world.Start( new FirstSim() );

        var systems = world.Inspect( InspectorQuery.Nothing ).systems![0].children!;

        Assert.Equal( ["0", "1"], systems.Select( system => system.key ) );
    }
}
