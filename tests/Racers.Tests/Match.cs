using Engine;
using MintECS;

namespace Racers.Tests;

/// <summary>
/// A sim-only match for tests: the game's own sim systems and setup, run headless on a grid with two units.
/// </summary>
static class Match
{
    public const int TICKS_PER_SECOND = 60;

    public static readonly RaceConfig RACE = new()
    {
        width = 9,
        height = 3,
        units = [ new RaceConfig.UnitDef { id = "Car", x = 2, y = 2 }, new RaceConfig.UnitDef { id = "Car", x = 0, y = 0 } ],
    };

    /// <summary>Commands by the tick they apply on. Two land on tick 40 to check that order within a tick holds.</summary>
    public static readonly ( long tick, int x, int y )[] SCRIPT =
    [
        ( 3, 4, 1 ),
        ( 17, 8, 2 ),
        ( 40, 0, 0 ),
        ( 40, 5, 1 ),
        ( 41, 99, 99 ),
        ( 75, 1, 2 ),
        ( 118, 7, 0 ),
    ];

    public static string CONTENT_ROOT => Path.Combine( AppContext.BaseDirectory, "Content" );

    static Match ()
        => HeadlessRunner.Setup( CONTENT_ROOT );

    public static World Create ()
        => HeadlessRunner.CreateMatch( RACE, TICKS_PER_SECOND );

    /// <summary>Starts the sim systems on a world, fresh or loaded.</summary>
    public static World StartSystems ( World world )
    {
        SimSystems.Start( world );
        return world;
    }

    /// <summary>Runs <paramref name="ticks"/> ticks, sending the script's commands before the tick they apply on, and returns each tick's checksum.</summary>
    public static List< TickChecksum > Run ( World world, int ticks, bool sendScript = true )
    {
        var checksums = new List< TickChecksum >();
        for ( int i = 0; i < ticks; i++ )
        {
            if ( sendScript )
            {
                foreach ( var ( tick, x, y ) in SCRIPT )
                {
                    if ( tick == world.tick + 1 )
                        world.Send( new SelectCell { x = x, y = y } );
                }
            }

            world.FixedTick();
            checksums.Add( new TickChecksum( world.tick, world.Checksum() ) );
        }

        return checksums;
    }

    public static Replay Record ( int ticks )
    {
        var world = Create();
        var recorder = new ReplayRecorder( TICKS_PER_SECOND / 4 );
        recorder.Attach( world );
        Run( world, ticks );
        return Replay.FromRecording( recorder, world, RACE );
    }

    /// <summary>Plays a replay into a fresh match until it finishes or desyncs, and returns each tick's checksum.</summary>
    public static ( ReplayPlayer player, List< TickChecksum > checksums ) Play ( Replay replay, World? world = null )
    {
        world ??= Create();
        var player = replay.CreatePlayer();
        player.Attach( world );

        var checksums = new List< TickChecksum >();
        while ( !player.isFinished && player.desync == null )
        {
            world.FixedTick();
            checksums.Add( new TickChecksum( world.tick, world.Checksum() ) );
        }

        player.Detach();
        return ( player, checksums );
    }
}
