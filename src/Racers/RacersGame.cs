using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public sealed class RacersGame : Game
{
    public static readonly GameOptions Options = new();
    
    // Debug Reporting
    double nextReport;
    SpriteBatch? batch; // read in Update, after the previous frame's End() has flushed everything
    int frames;

    // World
    uint canvas;
    uint debugCanvas;
    uint statsText;
    uint statsShadow;

    World world;

    protected override void Load ()
    {
        Toolbox.Setup( this );

        world = new World();
        world.Start( new PrefabSystem() );
        world.Start( new RenderSystem() );

        canvas = world.Create( "Canvas",
            new Canvas {},
            new SortOrder { value = 0 }
        );

        debugCanvas = world.Create( "Debug Canvas",
            new Canvas {},
            new SortOrder { value = 9999 }
        );


        var prefab = world.Get< PrefabSystem >();

        var rive = prefab.CreateFromData( "CloudyRoad", canvas );
        world.GetOrAdd< SortOrder >( rive ).value = -100;

        var car = prefab.CreateFromData( "Car2", canvas );
        world.Get< Position >( car ).value = new Vector2( 10, 10 );

        var solid = world.Get< PrefabSystem >().CreateSolid( "Test Solid", 40, 20, new Color( 1f, 0.3f, 0.3f ), canvas );
        world.Get< Position >( solid ).value = new Vector2( 80, 10 );

        statsShadow = world.Get< PrefabSystem >().CreateText( "Stats Shadow", "", "Crates", 8, Color.Black.WithAlpha( 0.6f ), debugCanvas );
        world.Get< Position >( statsShadow ).value = new Vector2( 6, 11 );
        world.Get< Rect >( statsShadow ).pivot = new Vector2( 0, 1 );

        statsText = world.Get< PrefabSystem >().CreateText( "Stats", "", "Crates", 8, Color.White, debugCanvas );
        world.Get< Position >( statsText ).value = new Vector2( 5, 12 );
        world.Get< Rect >( statsText ).pivot = new Vector2( 0, 1 );
        world.Add( statsText, new SortOrder { value = 1 } );
    }

    protected override void OnResize ()
    {
        
    }

    protected override void Unload ()
    {
        Toolbox.I.load.Dispose();
    }

    protected override void OnPointer ( PointerEvent e )
    {
        if ( e.action == PointerAction.Down )
        {
            // int lane = Math.Clamp( ( int )MathF.Round( ( 0.58f - e.position.Y / graphics.height ) / 0.09f ), 0, LANES - 1 );
            // Spawn( lane, e.position.X );
        }
    }

    protected override void OnKey ( Key key, bool down )
    {
        if ( down && key == Key.Escape )
            Exit();
    }

    protected override void Update ( GameTime time )
    {
        world.Tick();

        frames++;
        if ( time.totalSeconds >= nextReport )
        {
            if ( nextReport > 0 )
            {
                var stats = $"{frames / 2f:0} fps {batch?.drawCalls} draw calls";
                world.Get< DrawText >( statsText ).text = stats;
                world.Get< DrawText >( statsShadow ).text = stats;
            }
            frames = 0;
            nextReport = time.totalSeconds + 2;
        }
    }

    protected override void Draw ( SpriteBatch batch )
    {
        world.Draw( batch );
        this.batch = batch;
    }
}
