using Engine;
using MintECS;

namespace Racers;

public class ReplayResult
{
    public World world;
    public long endTick;
    public int recordedChecksums;
    public int matchedChecksums;
    public ReplayDesync? desync;

    public long tick => world.tick;
    public bool passed => desync == null && tick == endTick && matchedChecksums == recordedChecksums;
}

public static class HeadlessRunner
{
    public static void Setup ( string contentRoot = null )
    {
        var root = Path.TrimEndingDirectorySeparator( Path.GetFullPath( contentRoot ?? ContentManager.DefaultRoot() ) ) + Path.DirectorySeparatorChar;
        if ( Toolbox.I != null && Toolbox.I.content.root == root )
            return;

        Toolbox.Setup( new ContentManager( root ) );
    }

    public static World CreateMatch ( RaceConfig race, int ticksPerSecond )
    {
        if ( Toolbox.I == null )
            throw new InvalidOperationException( "Call HeadlessRunner.Setup before creating a match." );

        var world = new World( ticksPerSecond );
        SimSystems.Start( world );
        world.Get< RaceSystem >().Setup( race );
        return world;
    }

    public static ReplayResult Play ( Replay replay )
    {
        var world = CreateMatchFor( replay );
        var player = replay.CreatePlayer();
        player.Attach( world );

        while ( !player.isFinished && player.desync == null )
            world.FixedTick();

        player.Detach();
        return new ReplayResult
        {
            world = world,
            endTick = replay.header.endTick,
            recordedChecksums = replay.checksums.Count,
            matchedChecksums = player.matchedChecksums,
            desync = player.desync,
        };
    }

    public static Replay Rerecord ( Replay replay )
    {
        var world = CreateMatchFor( replay );
        var recorder = new ReplayRecorder( replay.header.ticksPerSecond );
        recorder.Attach( world );

        var player = new ReplayPlayer( replay.commands, [], replay.header.endTick );
        player.Attach( world );
        while ( !player.isFinished )
            world.FixedTick();

        player.Detach();

        var rerecorded = Replay.FromRecording( recorder, world, replay.header.race );
        recorder.Detach();
        rerecorded.header.seed = replay.header.seed;
        return rerecorded;
    }

    static World CreateMatchFor ( Replay replay )
    {
        if ( replay.header.startTick != 0 )
            throw new InvalidDataException( $"The replay starts at tick { replay.header.startTick }. Only replays recorded from the start of a match can be played from its setup." );

        return CreateMatch( replay.header.race, replay.header.ticksPerSecond );
    }
}
