using Engine;
using MintECS;

namespace Racers.Tests;

public class WorldSaveTests
{
    [Fact]
    public void SavingALoadedWorldGivesTheSameText ()
    {
        var world = Match.Create();
        Match.Run( world, 30 );
        using ( world.EnterViewPhase() )
            world.Create( "Label", new Position { value = new Vector2( 1.5f, -2.25f ) }, new DrawText { text = "hi" } );

        string saved = world.Save();
        var loaded = World.Load( saved );

        Assert.Equal( saved, loaded.Save() );
        Assert.Equal( world.Checksum(), loaded.Checksum() );
        Assert.Equal( world.tick, loaded.tick );
        Assert.Equal( world.nextEntityId, loaded.nextEntityId );
        Assert.Equal( world.nextViewEntityId, loaded.nextViewEntityId );
    }

    /// <summary>Rule 7: systems keep no state of their own, so stopping mid-match and loading into new systems changes nothing.</summary>
    [Fact]
    public void LoadingMidMatchMatchesAnUninterruptedRun ()
    {
        const int TOTAL = 150;
        var uninterrupted = Match.Run( Match.Create(), TOTAL );

        foreach ( int saveAt in new[] { 0, 1, 3, 39, 40, 41, 100 } )
        {
            var first = Match.Create();
            var before = Match.Run( first, saveAt );

            var second = Match.StartSystems( World.Load( first.Save() ) );
            var after = Match.Run( second, TOTAL - saveAt );

            Assert.Equal( uninterrupted, before.Concat( after ) );
        }
    }

    [Fact]
    public void PendingCommandsSurviveALoad ()
    {
        var world = Match.Create();
        Match.Run( world, 5, sendScript: false );
        world.Send( new SelectCell { x = 6, y = 1 } );

        var loaded = Match.StartSystems( World.Load( world.Save() ) );
        world.FixedTick();
        loaded.FixedTick();

        Assert.Equal( world.Checksum(), loaded.Checksum() );

        var unit = loaded.GetSingleEntity( new FilterBuilder().Include< Unit >() );
        var cell = loaded.GetRelated< InCell >( unit );
        Assert.Equal( new Vector2Int( 6, 1 ), loaded.Get< Cell >( cell ).coords );
    }

    [Fact]
    public void SimStateLeavesOutTheView ()
    {
        var world = Match.Create();
        Match.Run( world, 20 );
        string simState = world.SaveSimState();

        using ( world.EnterViewPhase() )
            world.Create( "Label", new DrawText { text = "hi" } );

        Assert.Equal( simState, world.SaveSimState() );
        Assert.DoesNotContain( "DrawText", simState );
        Assert.Contains( "InCell", simState );
    }

    [Fact]
    public void LoadRejectsASimComponentOnAViewEntity ()
    {
        string yaml = $"""
            ticksPerSecond: 60
            nextEntityId: 1
            nextViewEntityId: { World.VIEW_ENTITY_ID_START + 1 }
            entities:
            - id: { World.VIEW_ENTITY_ID_START }
              components:
                Unit: {"{}"}
            """;

        var exception = Assert.Throws< InvalidDataException >( () => World.Load( yaml ) );
        Assert.Contains( "view entity", exception.Message );
    }

    [Fact]
    public void LoadRejectsAnIdPastItsCounter ()
    {
        string yaml = """
            ticksPerSecond: 60
            nextEntityId: 3
            entities:
            - id: 3
              components: {}
            """;

        Assert.Throws< InvalidDataException >( () => World.Load( yaml ) );
    }
}
