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
    
    World world;
    CommandRecorder recorder = new();

    protected override void Load ()
    {
        Toolbox.Setup( this );

        world = new World( ticksPerSecond );
        recorder.Attach( world );

        // Utilities
        world.Start( new TimeSystem() );
        world.Start( new PrefabSystem() );
        
        // Sim
        world.Start( new GridSystem() );
        world.Start( new RaceSystem() );

        // Input
        world.Start( new PointerSystem() );
        world.Start( new ButtonSystem() );

        // Views
        world.Start( new GameViewSystem() );

        world.Start( new GridViewSystem() );
        world.Start( new UnitViewSystem() );

        world.Start( new CutsceneSystem() );
        world.Start( new SpriteAnimationSystem() );

        world.Start( new CoroutineSystem() );
        world.Start( new RenderSystem() );


        var raceConfig = Toolbox.I.load.Race( "test" );
        world.Get< RaceSystem >().Setup( raceConfig );

        using ( world.EnterViewPhase() )
            world.Get< GameViewSystem >().Setup( graphics, raceConfig );

        world.Get< GameViewSystem >().uiState.Set< Race_DefaultState >();
    }

    void LoadView ()
    {
        


        
    }

    protected override void OnResize ()
    {
        
    }

    protected override void Unload ()
    {
        recorder.Detach();
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
    }

    protected override void FixedUpdate ()
    {
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
