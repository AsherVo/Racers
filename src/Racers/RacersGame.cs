using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine;

namespace Racers;

public sealed class RacersGame : Game
{
    public static readonly GameOptions Options = new();

    const int LANES = 6;

    struct Car
    {
        public Vector2 position;
        public float speed, hue;
        public int lane, number;
    }

    readonly List< Car > cars = [];
    readonly Random random = new( 1234 );
    Texture carTexture = null!;
    Font titleFont = null!, smallFont = null!;
    RiveFile rive = null!;
    RiveInstance background = null!, inset = null!;
    RectangleF backgroundRect;
    float insetSpin;
    string stats = "";
    double nextReport;
    SpriteBatch? batch; // read in Update, after the previous frame's End() has flushed everything
    int frames;

    protected override void Load ()
    {
        carTexture = content.LoadTexture( "car_1.png", TextureFilter.PixelArt );
        titleFont = content.LoadFont( "Crates.ttf", 30, graphics.pixelScale );
        smallFont = content.LoadFont( "Crates.ttf", 8, graphics.pixelScale );

        rive = content.LoadRive( "cloudyroad.riv" );

        // Cover the screen with the background, rendered at the resolution it's shown at.
        var probe = rive.CreateInstance( "Background" );
        var artboard = probe.size;
        probe.Dispose();
        float cover = MathF.Max( graphics.width / artboard.X, graphics.height / artboard.Y );
        background = rive.CreateInstance( "Background", resolution: MathF.Min( cover * graphics.pixelScale, 4096f / MathF.Max( artboard.X, artboard.Y ) ) );
        FitBackground();
        Log.Info( $"Rive artboard 'Background' is {artboard.X}x{artboard.Y}; texture {background.texture.width}x{background.texture.height}" );

        // A second, independent instance of the same artboard, drawn small.
        inset = rive.CreateInstance( "Background", resolution: 0.35f * cover * graphics.pixelScale );

        for ( int lane = 0; lane < LANES; lane++ )
            for ( int i = 0; i < 3; i++ )
                Spawn( lane, random.NextSingle() * graphics.width );
    }

    void FitBackground ()
    {
        var artboard = background.size;
        float cover = MathF.Max( graphics.width / artboard.X, graphics.height / artboard.Y );
        var size = artboard * cover;
        backgroundRect = new RectangleF( ( graphics.width - size.X ) / 2, ( graphics.height - size.Y ) / 2, size.X, size.Y );
    }

    protected override void OnResize ()
    {
        FitBackground();
        foreach ( ref var car in CollectionsMarshal.AsSpan( cars ) )
            car.position.Y = LaneY( car.lane );
    }

    protected override void Unload ()
    {
        background.Dispose();
        inset.Dispose();
        rive.Dispose();
        carTexture.Dispose();
        titleFont.Dispose();
        smallFont.Dispose();
    }

    float LaneY ( int lane ) => graphics.height * ( 0.58f - 0.09f * lane );

    void Spawn ( int lane, float x ) => cars.Add( new Car
    {
        position = new Vector2( x, LaneY( lane ) ),
        speed = 30f + random.NextSingle() * 80f,
        hue = random.NextSingle(),
        lane = lane,
        number = cars.Count + 1,
    } );

    protected override void OnPointer ( PointerEvent e )
    {
        if ( e.action == PointerAction.Down )
        {
            int lane = Math.Clamp( ( int )MathF.Round( ( 0.58f - e.position.Y / graphics.height ) / 0.09f ), 0, LANES - 1 );
            Spawn( lane, e.position.X );
        }
    }

    protected override void OnKey ( Key key, bool down )
    {
        if ( down && key == Key.Escape )
            Exit();
    }

    protected override void Update ( GameTime time )
    {
        float wrap = graphics.width + 60;
        foreach ( ref var car in CollectionsMarshal.AsSpan( cars ) )
        {
            car.position.X += car.speed * time.deltaSeconds;
            if ( car.position.X > graphics.width + 30 )
                car.position.X -= wrap;
        }

        insetSpin += 0.3f * time.deltaSeconds;

        frames++;
        if ( time.totalSeconds >= nextReport )
        {
            if ( nextReport > 0 )
                stats = $"{frames / 2f:0} fps  {cars.Count} cars  {batch?.drawCalls} draw calls";
            frames = 0;
            nextReport = time.totalSeconds + 2;
        }
    }

    protected override void Draw ( SpriteBatch batch )
    {
        // 1. Rive, opaque, behind everything.
        batch.Draw( background, backgroundRect, Color.White );

        // 2. Text over Rive, with a translucent drop shadow.
        const string TITLE = "RACERS";
        var titleSize = titleFont.MeasureString( TITLE );
        var titlePos = new Vector2( ( graphics.width - titleSize.X ) / 2, graphics.height - 12 );
        batch.DrawString( titleFont, TITLE, titlePos + new Vector2( 2, -2 ), Color.Black.WithAlpha( 0.45f ) );
        batch.DrawString( titleFont, TITLE, titlePos, new Color( 1f, 0.85f, 0.3f ) );

        // 3. Cars; halfway through the lanes, 4. a translucent, rotating Rive inset that the
        // remaining lanes then drive over.
        var carOrigin = new Vector2( carTexture.width / 2f, carTexture.height / 2f );
        for ( int lane = 0; lane < LANES; lane++ )
        {
            if ( lane == LANES / 2 )
                DrawInset( batch );

            foreach ( ref readonly var car in CollectionsMarshal.AsSpan( cars ) )
            {
                if ( car.lane == lane )
                    batch.Draw( carTexture, car.position, null, Color.FromHsv( car.hue, 0.5f, 1f ), 0f, carOrigin, Vector2.One );
            }
        }

        // 5. Number tags, all together on top so they share one batch.
        foreach ( ref readonly var car in CollectionsMarshal.AsSpan( cars ) )
        {
            string tag = car.number.ToString();
            var tagPos = car.position + new Vector2( -smallFont.MeasureString( tag ).X / 2, 24 );
            batch.DrawString( smallFont, tag, tagPos, Color.White.WithAlpha( 0.8f ) );
        }

        // 6. HUD text on top of everything.
        batch.DrawString( smallFont, stats, new Vector2( 6, 11 ), Color.Black.WithAlpha( 0.6f ) );
        batch.DrawString( smallFont, stats, new Vector2( 5, 12 ), Color.White );

        this.batch = batch;
    }

    void DrawInset ( SpriteBatch batch )
    {
        float scale = 0.35f * backgroundRect.Width / inset.width;
        var center = new Vector2( graphics.width * 0.72f, graphics.height * 0.4f );
        batch.Draw( inset, center, Color.White.WithAlpha( 0.75f ), MathF.Sin( insetSpin ) * 0.35f, inset.size / 2, new Vector2( scale ) );
    }
}
