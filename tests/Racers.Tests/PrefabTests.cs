using Engine;
using MintECS;

namespace Racers.Tests;

public class PrefabTests
{
    static uint FirstCell ( World world )
        => world.Get< GridSystem >().GetCell( new Vector2Int( 0, 0 ) );

    [Fact]
    public void SimPrefabsCarryNoViewComponents ()
    {
        var world = Match.Create();
        var cell = FirstCell( world );

        Assert.Equal( "Cell", world.Get< DataId >( cell ).id );
        Assert.False( world.Has< ViewDataId >( cell ) );
        Assert.False( world.Has< Position >( cell ) );
        Assert.False( world.Has< DrawRive >( cell ) );
    }

    [Fact]
    public void TheViewDressesSimPrefabsWithoutChangingSimState ()
    {
        var world = Match.Create();
        var cell = FirstCell( world );
        var checksum = world.Checksum();

        world.Start( new PrefabViewSystem() );

        Assert.Equal( "Cell", world.Get< ViewDataId >( cell ).id );
        Assert.True( world.Has< Position >( cell ) );
        Assert.True( world.Has< DrawRive >( cell ) );
        Assert.Equal( checksum, world.Checksum() );
    }

    [Fact]
    public void SimPrefabsCreatedAfterTheViewStartsAreDressed ()
    {
        var world = Match.Create();
        world.Start( new PrefabViewSystem() );

        var car = world.Get< PrefabSystem >().CreateFromData( "Car" );

        Assert.Equal( "Car", world.Get< ViewDataId >( car ).id );
        Assert.True( world.Has< DrawSprite >( car ) );
        Assert.True( world.Has< Unit >( car ) );
    }

    [Fact]
    public void ALoadedWorldKeepsItsDressing ()
    {
        var world = Match.Create();
        world.Start( new PrefabViewSystem() );

        var cell = FirstCell( world );
        world.Get< Position >( cell ).value = new Vector2( 3f, 4f );

        var loaded = Match.StartSystems( World.Load( world.Save() ) );
        loaded.Start( new PrefabViewSystem() );

        Assert.Equal( 3f, loaded.Get< Position >( cell ).value.x );
        Assert.Equal( 4f, loaded.Get< Position >( cell ).value.y );
    }

    [Fact]
    public void ViewEntitiesGetAViewIdAndNoSimComponents ()
    {
        var world = Match.Create();
        world.Start( new PrefabViewSystem() );

        uint car;
        using ( world.EnterViewPhase() )
            car = world.Get< PrefabViewSystem >().CreateFromData( "Car" );

        Assert.True( World.IsViewEntity( car ) );
        Assert.Equal( "Car", world.Get< ViewDataId >( car ).id );
        Assert.False( world.Has< DataId >( car ) );
        Assert.False( world.Has< Unit >( car ) );
    }
}
