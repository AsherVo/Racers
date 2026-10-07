using RectangleF = System.Drawing.RectangleF;
using System.Numerics;
using Engine;

namespace HelloSprite;

/// <summary>Bouncing, spinning, tinted sprites. Click or tap to spawn more; Escape quits.</summary>
public sealed class HelloSpriteGame : Game
{
    public static readonly GameOptions Options = new() { Title = "Hello Sprite" };

    struct Sprite
    {
        public Vector2 Position, Velocity;
        public float Rotation, Spin, Hue, Scale;
    }

    readonly List<Sprite> _sprites = [];
    readonly Random _random = new(1234);
    Texture _texture = null!;
    double _nextReport;
    int _frames;

    protected override void Load()
    {
        _texture = Content.LoadTexture("sprite.png");
        Spawn(new Vector2(Graphics.Width / 2f, Graphics.Height / 2f), 300);
    }

    protected override void Unload() => _texture.Dispose();

    protected override void OnPointer(PointerEvent e)
    {
        if (e.Action == PointerAction.Down)
            Spawn(e.Position, 200);
    }

    protected override void OnKey(Key key, bool down)
    {
        if (down && key == Key.Escape)
            Exit();
    }

    void Spawn(Vector2 at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = _random.NextSingle() * MathF.Tau;
            _sprites.Add(new Sprite
            {
                Position = at,
                Velocity = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (80f + _random.NextSingle() * 320f),
                Spin = (_random.NextSingle() - 0.5f) * 6f,
                Hue = _random.NextSingle(),
                Scale = 0.4f + _random.NextSingle() * 0.8f,
            });
        }
    }

    protected override void Update(GameTime time)
    {
        float w = Graphics.Width, h = Graphics.Height;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_sprites);
        foreach (ref var s in span)
        {
            s.Position += s.Velocity * time.DeltaSeconds;
            s.Rotation += s.Spin * time.DeltaSeconds;
            if (s.Position.X < 0 || s.Position.X > w) s.Velocity.X = -s.Velocity.X;
            if (s.Position.Y < 0 || s.Position.Y > h) s.Velocity.Y = -s.Velocity.Y;
            s.Position = Vector2.Clamp(s.Position, Vector2.Zero, new Vector2(w, h));
        }

        _frames++;
        if (time.TotalSeconds >= _nextReport)
        {
            if (_nextReport > 0)
                Log.Info($"{_frames / 2f:0.0} fps, {_sprites.Count} sprites");
            _frames = 0;
            _nextReport = time.TotalSeconds + 2;
        }
    }

    protected override void Draw(SpriteBatch batch)
    {
        // Frame the virtual resolution so letterboxing is visible on any screen shape.
        batch.FillRectangle(new RectangleF(0, 0, Graphics.Width, Graphics.Height), new Color(0.16f, 0.18f, 0.26f));
        batch.FillRectangle(new RectangleF(0, Graphics.Height - 8, Graphics.Width, 8), new Color(0.9f, 0.5f, 0.2f));

        var origin = new Vector2(_texture.Width / 2f, _texture.Height / 2f);
        foreach (ref readonly var s in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_sprites))
        {
            batch.Draw(_texture, s.Position, null, Color.FromHsv(s.Hue, 0.65f, 1f), s.Rotation, origin, new Vector2(s.Scale));
        }
    }
}
