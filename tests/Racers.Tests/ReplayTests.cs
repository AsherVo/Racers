using Engine;
using MintECS;

namespace Racers.Tests;

public class ReplayTests
{
    const int TICKS = 150;

    [Fact]
    public void RecordingCapturesCommandsAndChecksums ()
    {
        var replay = Match.Record( TICKS );

        Assert.Equal( Match.SCRIPT.Select( step => step.tick ), replay.commands.Select( command => command.tick ) );
        Assert.Equal( Match.SCRIPT.Select( step => step.x ), replay.commands.Cast< SelectCell >().Select( command => command.x ) );

        int interval = Match.TICKS_PER_SECOND / 4;
        var expectedTicks = Enumerable.Range( 0, TICKS / interval + 1 ).Select( i => ( long )i * interval ).ToList();
        if ( expectedTicks[ ^1 ] != TICKS )
            expectedTicks.Add( TICKS );

        Assert.Equal( expectedTicks, replay.checksums.Select( checksum => checksum.tick ) );
        Assert.Equal( 0, replay.header.startTick );
        Assert.Equal( TICKS, replay.header.endTick );
    }

    [Fact]
    public void RecordingNeverChangesTheSim ()
    {
        var withRecorder = Match.Create();
        new ReplayRecorder( 1 ).Attach( withRecorder );

        Assert.Equal( Match.Run( Match.Create(), TICKS ), Match.Run( withRecorder, TICKS ) );
    }

    [Fact]
    public void RecordedCommandsAreCopies ()
    {
        var world = Match.Create();
        var recorder = new ReplayRecorder( 60 );
        recorder.Attach( world );

        var command = new SelectCell { x = 3, y = 1 };
        world.Send( command );
        world.FixedTick();
        command.x = 8;

        var recorded = Assert.IsType< SelectCell >( Assert.Single( recorder.commands ) );
        Assert.NotSame( command, recorded );
        Assert.Equal( 3, recorded.x );
        Assert.Equal( 1, recorded.tick );
    }

    [Fact]
    public void ReplayFilesRoundTrip ()
    {
        var replay = Match.Record( TICKS );
        string path = Path.Combine( Path.GetTempPath(), $"racers-replay-{ Guid.NewGuid() }.yaml" );
        try
        {
            replay.Write( path );
            var read = Replay.Read( path );

            Assert.Equal( replay.ToYaml(), read.ToYaml() );
            Assert.Equal( Match.RACE.width, read.header.race.width );
            Assert.Equal( replay.checksums, read.checksums );
        }
        finally
        {
            File.Delete( path );
        }
    }

    [Fact]
    public void AReplayPlaysBackWithEveryChecksumMatching ()
    {
        var replay = Replay.FromYaml( Match.Record( TICKS ).ToYaml() );
        var ( player, _ ) = Match.Play( replay );

        Assert.Null( player.desync );
        Assert.Equal( replay.checksums.Count, player.matchedChecksums );
        Assert.Equal( 0, player.droppedCommands );
    }

    [Fact]
    public void PlayingTheSameReplayTwiceGivesTheSameChecksums ()
    {
        var replay = Match.Record( TICKS );
        var uninterrupted = Match.Run( Match.Create(), TICKS );

        var ( _, first ) = Match.Play( replay );
        var ( _, second ) = Match.Play( replay );

        Assert.Equal( uninterrupted, first );
        Assert.Equal( first, second );
    }

    [Fact]
    public void AWrongChecksumIsReportedAtItsTick ()
    {
        var replay = Match.Record( TICKS );
        var tampered = replay.checksums[ 3 ];
        replay.checksums[ 3 ] = new TickChecksum( tampered.tick, tampered.checksum ^ 1 );

        ReplayDesync? reported = null;
        var world = Match.Create();
        var player = replay.CreatePlayer();
        player.onDesync += desync => reported = desync;
        player.Attach( world );
        while ( !player.isFinished && player.desync == null )
            world.FixedTick();

        Assert.Equal( tampered.tick, world.tick );
        Assert.Equal( new ReplayDesync( tampered.tick, tampered.checksum ^ 1, tampered.checksum ), reported );
        Assert.Equal( 3, player.matchedChecksums );
    }

    [Fact]
    public void ADifferentCommandDesyncsAtTheNextChecksum ()
    {
        var replay = Match.Record( TICKS );
        var changed = ( SelectCell )replay.commands[ 1 ];
        changed.x = 1;

        var ( player, _ ) = Match.Play( replay );

        long nextChecksumTick = replay.checksums.First( checksum => checksum.tick >= changed.tick ).tick;
        Assert.Equal( nextChecksumTick, player.desync?.tick );
    }

    [Fact]
    public void CommandsFromTheViewAreDroppedDuringPlayback ()
    {
        var replay = Match.Record( TICKS );
        var world = Match.Create();
        var player = replay.CreatePlayer();
        player.Attach( world );

        while ( !player.isFinished && player.desync == null )
        {
            if ( world.tick % 20 == 5 )
            {
                using ( world.EnterViewPhase() )
                    world.Send( new SelectCell { x = 8, y = 0 } );
            }

            world.FixedTick();
        }

        Assert.Null( player.desync );
        Assert.Equal( TICKS / 20 + 1, player.droppedCommands );
    }

    [Fact]
    public void PlaybackCanStartFromALoadedWorld ()
    {
        var replay = Match.Record( TICKS );

        var first = Match.Create();
        var firstPlayer = replay.CreatePlayer();
        firstPlayer.Attach( first );
        for ( int i = 0; i < 60; i++ )
            first.FixedTick();

        firstPlayer.Detach();

        var loaded = Match.StartSystems( World.Load( first.Save() ) );
        var ( player, _ ) = Match.Play( replay, loaded );

        Assert.Null( player.desync );
        Assert.Equal( TICKS, loaded.tick );
        Assert.Equal( Match.Run( Match.Create(), TICKS )[ ^1 ].checksum, loaded.Checksum() );
    }

    [Fact]
    public void OutOfOrderRecordingsAreRejected ()
    {
        var commands = new Command[] { new SelectCell { tick = 5 }, new SelectCell { tick = 4 } };
        Assert.Throws< ArgumentException >( () => new ReplayPlayer( commands, [], 10 ) );

        var checksums = new[] { new TickChecksum( 5, 1 ), new TickChecksum( 5, 2 ) };
        Assert.Throws< ArgumentException >( () => new ReplayPlayer( [], checksums, 10 ) );
    }
}
