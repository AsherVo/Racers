using RectangleF = System.Drawing.RectangleF;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

using Racers.Coroutines;

namespace Racers;

public sealed class RacersGame : Game
{
    public static readonly GameOptions Options = new();
    
    // Debug Reporting
    double nextReport;
    SpriteBatch? batch; // read in Update, after the previous frame's End() has flushed everything
    int frames;
    
    const string REPLAY_VARIABLE = "ENGINE_REPLAY";
    const string REPLAY_DIRECTORY = "replays";

    World world;
    RaceConfig race;
    ReplayRecorder recorder;

    string replayPath;
    ReplayPlayer player;
    bool loggedReplayEnd;

    protected override void Load ()
    {
        Toolbox.Setup( this );

        var replay = ReadReplayFromEnvironment();
        world = new World( replay?.header.ticksPerSecond ?? ticksPerSecond );

        world.Start( new TimeSystem() );
        SimSystems.Start( world );

        // Input
        world.Start( new PointerSystem() );
        world.Start( new RiveListSystem() );
        world.Start( new ButtonSystem() );

        // Views
        world.Start( new GameViewSystem() );

        world.Start( new GridViewSystem() );
        world.Start( new UnitViewSystem() );
        world.Start( new MenuViewSystem() );

        world.Start( new CutsceneSystem() );
        world.Start( new SpriteAnimationSystem() );

        world.Start( new CoroutineSystem() );
        world.Start( new RenderSystem() );


        race = replay?.header.race ?? Toolbox.I.load.Race( "test" );
        world.Get< RaceSystem >().Setup( race );

        using ( world.EnterViewPhase() )
            world.Get< GameViewSystem >().Setup( graphics, race );

        world.Get< GameViewSystem >().uiState.Set< Race_DefaultState >();

        recorder = new ReplayRecorder( world.ticksPerSecond );
        recorder.Attach( world );

        if ( replay != null )
        {
            player = replay.CreatePlayer();
            player.onDesync += OnDesync;
            player.Attach( world );
            Log.Info( $"Replay: playing { replayPath } to tick { player.endTick }." );
        }
    }

    Replay ReadReplayFromEnvironment ()
    {
        replayPath = Environment.GetEnvironmentVariable( REPLAY_VARIABLE );
        if ( string.IsNullOrEmpty( replayPath ) )
            return null;

        Replay replay;
        try
        {
            replay = Replay.Read( replayPath );
        }
        catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or YamlException or InvalidDataException )
        {
            Log.Error( $"Replay: couldn't read { replayPath }, so playing live.\n{ e.Message }" );
            return null;
        }

        if ( replay.header.ticksPerSecond != ticksPerSecond )
            Log.Warning( $"Replay: recorded at { replay.header.ticksPerSecond } ticks per second, but config.yaml says { ticksPerSecond }. Playback speed will differ; the outcome won't." );

        return replay;
    }

    void OnDesync ( ReplayDesync desync )
    {
        Log.Error( $"Replay: desync at { desync }. Pausing." );

        var dumpPath = $"{ replayPath }.desync-{ desync.tick }.yaml";
        try
        {
            File.WriteAllText( dumpPath, world.SaveSimState() );
            Log.Info( $"Replay: sim state at the desync written to { dumpPath }" );
        }
        catch ( Exception e )
        {
            Log.Error( $"Replay: couldn't write the desync report to { dumpPath }: { e.Message }" );
        }
    }

    void SaveReplay ()
    {
        var path = Path.Combine( Environment.CurrentDirectory, REPLAY_DIRECTORY, $"replay-{ DateTime.Now:yyyyMMdd-HHmmss}.yaml" );
        try
        {
            Replay.FromRecording( recorder, world, race ).Write( path );
            Log.Info( $"Replay: saved ticks { recorder.startTick }-{ world.tick } to { path }" );
        }
        catch ( Exception e )
        {
            Log.Error( $"Replay: couldn't save to { path }: { e.Message }" );
        }
    }

    void LoadView ()
    {
        


        
    }

    protected override void OnResize ()
    {
        
    }

    protected override void Unload ()
    {
        player?.Detach();
        recorder?.Detach();
        Toolbox.I.load.Dispose();
    }

    protected override void OnPointer ( PointerEvent e )
    {
        using var view = world.EnterViewPhase();

        if ( e.action == PointerAction.Move )
            world.Send( new PointerHover { position = e.position } );

        if ( e.action == PointerAction.Up )
            world.Send( new PointerClick { position = e.position } );

        if ( e.action == PointerAction.Down )
        {
            world.Send( new PointerDown { position = e.position } );

            // int lane = Math.Clamp( ( int )MathF.Round( ( 0.58f - e.position.y / graphics.height ) / 0.09f ), 0, LANES - 1 );
            // Spawn( lane, e.position.x );
        }
    }

    protected override void OnKey ( Key key, bool down )
    {
        using var view = world.EnterViewPhase();

        world.Send( new KeyInput { key = key, down = down } );

        if ( down && key == Key.Escape )
            Exit();

        if ( down && key == Key.F5 )
            SaveReplay();
    }

    protected override void FixedUpdate ()
    {
        if ( player != null && ( player.isFinished || player.desync != null ) )
        {
            if ( player.isFinished && !loggedReplayEnd )
                Log.Info( $"Replay: finished at tick { world.tick }, { player.matchedChecksums } checksums matched." );

            loggedReplayEnd = player.isFinished;
            return;
        }

        world.FixedTick();
    }

    protected override void Update ( FrameTime time )
    {
        world.Update( time );

        frames++;
        var total = world.Get< TimeSystem >().GetTotal( Time.App );
        var ticks = world.tick;
        if ( total >= nextReport )
        {
            if ( nextReport > 0 )
            {
                var stats = $"{frames / 2f:0} fps / {batch?.drawCalls} draw calls / { ticks } ticks";
                world.Get< GameViewSystem >().DrawDebugStats( stats );
            }
            frames = 0;
            nextReport = total + 2;
        }
    }

    protected override void Draw ( SpriteBatch batch )
    {
        world.Draw( batch );
        this.batch = batch;
    }
}
