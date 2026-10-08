using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using System.Runtime.InteropServices;
using Engine;

namespace HelloSprite;

/// <summary>
/// Sprites, text and Rive interleaved with transparency: a Rive background, a title, cars with
/// name tags, and a translucent spinning Rive inset that half the lanes drive under and half over.
/// Click or tap to add a car; Escape quits.
/// </summary>
public sealed class HelloSpriteGame : Game
{
    public static readonly GameOptions Options = new() { Title = "Hello Sprite" };

    // Display pixels per virtual pixel to render Rive at, so it stays sharp on Retina screens.
    const float Density = 2f;
    const int Lanes = 6;

    struct Car
    {
        public Vector2 Position;
        public float Speed, Hue;
        public int Lane, Number;
    }

    readonly List< Car > _cars = [];
    readonly Random _random = new( 1234 );
    Texture _carTexture = null!;
    Font _titleFont = null!, _smallFont = null!;
    RiveFile _rive = null!;
    RiveInstance _background = null!, _inset = null!;
    RectangleF _backgroundRect;
    float _insetSpin;
    string _stats = "";
    double _nextReport;
    SpriteBatch? _batch; // read in Update, after the previous frame's End() has flushed everything
    int _frames;

    protected override void Load ()
    {
        _carTexture = Content.LoadTexture( "car_1.png", TextureFilter.PixelArt );
        _titleFont = Content.LoadFont( "Crates.ttf", 96 );
        _smallFont = Content.LoadFont( "Crates.ttf", 20 );

        _rive = Content.LoadRive( "cloudyroad.riv" );

        // Cover the screen with the background, rendered at the resolution it's shown at.
        var probe = _rive.CreateInstance( "Background" );
        var artboard = probe.Size;
        probe.Dispose();
        float cover = MathF.Max( Graphics.Width / artboard.X, Graphics.Height / artboard.Y );
        _background = _rive.CreateInstance( "Background", resolution: MathF.Min( cover * Density, 4096f / MathF.Max( artboard.X, artboard.Y ) ) );
        var size = artboard * cover;
        _backgroundRect = new RectangleF( ( Graphics.Width - size.X ) / 2, ( Graphics.Height - size.Y ) / 2, size.X, size.Y );
        Log.Info( $"Rive artboard 'Background' is {artboard.X}x{artboard.Y}; texture {_background.Texture.Width}x{_background.Texture.Height}" );

        // A second, independent instance of the same artboard, drawn small.
        _inset = _rive.CreateInstance( "Background", resolution: 0.35f * cover * Density );

        for ( int lane = 0; lane < Lanes; lane++ )
            for ( int i = 0; i < 3; i++ )
                Spawn( lane, _random.NextSingle() * Graphics.Width );
    }

    protected override void Unload ()
    {
        _background.Dispose();
        _inset.Dispose();
        _rive.Dispose();
        _carTexture.Dispose();
        _titleFont.Dispose();
        _smallFont.Dispose();
    }

    float LaneY ( int lane ) => Graphics.Height * ( 0.42f + 0.09f * lane );

    void Spawn ( int lane, float x ) => _cars.Add( new Car
    {
        Position = new Vector2( x, LaneY( lane ) ),
        Speed = 90f + _random.NextSingle() * 260f,
        Hue = _random.NextSingle(),
        Lane = lane,
        Number = _cars.Count + 1,
    } );

    protected override void OnPointer ( PointerEvent e )
    {
        if ( e.Action == PointerAction.Down )
        {
            int lane = Math.Clamp( ( int )MathF.Round( ( e.Position.Y / Graphics.Height - 0.42f ) / 0.09f ), 0, Lanes - 1 );
            Spawn( lane, e.Position.X );
        }
    }

    protected override void OnKey ( Key key, bool down )
    {
        if ( down && key == Key.Escape )
            Exit();
    }

    protected override void Update ( GameTime time )
    {
        float wrap = Graphics.Width + 200;
        foreach ( ref var car in CollectionsMarshal.AsSpan( _cars ) )
        {
            car.Position.X += car.Speed * time.DeltaSeconds;
            if ( car.Position.X > Graphics.Width + 100 )
                car.Position.X -= wrap;
        }

        _insetSpin += 0.3f * time.DeltaSeconds;

        _frames++;
        if ( time.TotalSeconds >= _nextReport )
        {
            if ( _nextReport > 0 )
                _stats = $"{_frames / 2f:0} fps  {_cars.Count} cars  {_batch?.DrawCalls} draw calls";
            _frames = 0;
            _nextReport = time.TotalSeconds + 2;
        }
    }

    protected override void Draw ( SpriteBatch batch )
    {
        // 1. Rive, opaque, behind everything.
        batch.Draw( _background, _backgroundRect, Color.White );

        // 2. Text over Rive, with a translucent drop shadow.
        const string title = "RACERS";
        var titleSize = _titleFont.MeasureString( title );
        var titlePos = new Vector2( ( Graphics.Width - titleSize.X ) / 2, 40 );
        batch.DrawString( _titleFont, title, titlePos + new Vector2( 5, 6 ), Color.Black.WithAlpha( 0.45f ) );
        batch.DrawString( _titleFont, title, titlePos, new Color( 1f, 0.85f, 0.3f ) );

        // 3. Cars; halfway through the lanes, 4. a translucent, rotating Rive inset that the
        // remaining lanes then drive over.
        var carOrigin = new Vector2( _carTexture.Width / 2f, _carTexture.Height / 2f );
        for ( int lane = 0; lane < Lanes; lane++ )
        {
            if ( lane == Lanes / 2 )
                DrawInset( batch );

            foreach ( ref readonly var car in CollectionsMarshal.AsSpan( _cars ) )
            {
                if ( car.Lane == lane )
                    batch.Draw( _carTexture, car.Position, null, Color.FromHsv( car.Hue, 0.5f, 1f ), 0f, carOrigin, new Vector2( 2f ) );
            }
        }

        // 5. Number tags, all together on top so they share one batch.
        foreach ( ref readonly var car in CollectionsMarshal.AsSpan( _cars ) )
        {
            string tag = car.Number.ToString();
            var tagPos = car.Position - new Vector2( _smallFont.MeasureString( tag ).X / 2, 52 );
            batch.DrawString( _smallFont, tag, tagPos, Color.White.WithAlpha( 0.8f ) );
        }

        // 6. HUD text on top of everything.
        batch.DrawString( _smallFont, _stats, new Vector2( 17, Graphics.Height - 31 ), Color.Black.WithAlpha( 0.6f ) );
        batch.DrawString( _smallFont, _stats, new Vector2( 16, Graphics.Height - 32 ), Color.White );

        _batch = batch;
    }

    void DrawInset ( SpriteBatch batch )
    {
        float scale = 0.35f * _backgroundRect.Width / _inset.Width;
        var center = new Vector2( Graphics.Width * 0.72f, Graphics.Height * 0.6f );
        batch.Draw( _inset, center, Color.White.WithAlpha( 0.75f ), MathF.Sin( _insetSpin ) * 0.35f, _inset.Size / 2, new Vector2( scale ) );
    }
}
