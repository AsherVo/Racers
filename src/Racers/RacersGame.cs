using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public sealed class RacersGame : Game
{
    public static readonly GameOptions Options = new();
    
    Font debugFont;
    string stats = "";
    double nextReport;
    SpriteBatch? batch; // read in Update, after the previous frame's End() has flushed everything
    int frames;

    uint canvas;
    uint debugCanvas;

    World world;

    protected override void Load ()
    {
        Toolbox.Setup( this );

        debugFont = Toolbox.I.load.Font( "Crates.ttf", 8 );

        world = new World();
        world.Start( new RenderSystem() );

        canvas = world.Create( "Canvas",
            new Canvas {},
            new SortOrder { value = 0 }
        );

        debugCanvas = world.Create( "Debug Canvas",
            new Canvas {},
            new SortOrder { value = 9999 }
        );

        world.Create( "Test Sprite",
            new OnCanvas { relation = canvas },
            new Sprite { path = "car_1.png" },
            new Position { value = new Vector2( 10, 10 ) }
        );
    }

    // void FitBackground ()
    // {
    //     var artboard = background.size;
    //     float cover = MathF.Max( graphics.width / artboard.X, graphics.height / artboard.Y );
    //     var size = artboard * cover;
    //     backgroundRect = new RectangleF( ( graphics.width - size.X ) / 2, ( graphics.height - size.Y ) / 2, size.X, size.Y );
    // }

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
                stats = $"{frames / 2f:0} fps {batch?.drawCalls} draw calls";
            frames = 0;
            nextReport = time.totalSeconds + 2;
        }
    }

    protected override void Draw ( SpriteBatch batch )
    {
        world.Draw( batch );

        // // 1. Rive, opaque, behind everything.
        // batch.Draw( background, backgroundRect, Color.White );

        // // 2. Text over Rive, with a translucent drop shadow.
        // const string TITLE = "RACERS";
        // var titleSize = titleFont.MeasureString( TITLE );
        // var titlePos = new Vector2( ( graphics.width - titleSize.X ) / 2, graphics.height - 12 );
        // batch.DrawString( titleFont, TITLE, titlePos + new Vector2( 2, -2 ), Color.Black.WithAlpha( 0.45f ) );
        // batch.DrawString( titleFont, TITLE, titlePos, new Color( 1f, 0.85f, 0.3f ) );

        // // 3. Cars; halfway through the lanes, 4. a translucent, rotating Rive inset that the
        // // remaining lanes then drive over.
        // var carOrigin = new Vector2( carTexture.width / 2f, carTexture.height / 2f );
        // for ( int lane = 0; lane < LANES; lane++ )
        // {
        //     if ( lane == LANES / 2 )
        //         DrawInset( batch );

        //     foreach ( ref readonly var car in CollectionsMarshal.AsSpan( cars ) )
        //     {
        //         if ( car.lane == lane )
        //             batch.Draw( carTexture, car.position, null, Color.FromHsv( car.hue, 0.5f, 1f ), 0f, carOrigin, Vector2.One );
        //     }
        // }

        // // 5. Number tags, all together on top so they share one batch.
        // foreach ( ref readonly var car in CollectionsMarshal.AsSpan( cars ) )
        // {
        //     string tag = car.number.ToString();
        //     var tagPos = car.position + new Vector2( -smallFont.MeasureString( tag ).X / 2, 24 );
        //     batch.DrawString( smallFont, tag, tagPos, Color.White.WithAlpha( 0.8f ) );
        // }

        // // 6. HUD text on top of everything.
        batch.DrawString( debugFont, stats, new Vector2( 6, 11 ), Color.Black.WithAlpha( 0.6f ) );
        batch.DrawString( debugFont, stats, new Vector2( 5, 12 ), Color.White );

        this.batch = batch;
    }

    // void DrawInset ( SpriteBatch batch )
    // {
    //     float scale = 0.35f * backgroundRect.Width / inset.width;
    //     var center = new Vector2( graphics.width * 0.72f, graphics.height * 0.4f );
    //     batch.Draw( inset, center, Color.White.WithAlpha( 0.75f ), MathF.Sin( insetSpin ) * 0.35f, inset.size / 2, new Vector2( scale ) );
    // }
}
