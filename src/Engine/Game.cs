using System.Numerics;

namespace Engine;

/// <summary>Base class for a game. The engine calls these hooks; hosts never touch them directly.</summary>
public abstract class Game
{
    public Graphics Graphics { get; internal set; } = null!;
    public ContentManager Content { get; internal set; } = null!;

    internal bool ExitRequested { get; private set; }

    public void Exit() => ExitRequested = true;

    protected internal virtual void Load() { }
    protected internal virtual void Update(GameTime time) { }
    protected internal virtual void Draw(SpriteBatch batch) { }
    protected internal virtual void Unload() { }

    /// <summary>Mouse and touch, in logical (virtual resolution) coordinates.</summary>
    protected internal virtual void OnPointer(PointerEvent e) { }
    protected internal virtual void OnKey(Key key, bool down) { }
}

public readonly record struct GameTime(double TotalSeconds, float DeltaSeconds);

public enum PointerAction { Down, Move, Up }

public readonly record struct PointerEvent(PointerAction Action, Vector2 Position);

public enum Orientation { Landscape, Portrait, Any }

public sealed record GameOptions
{
    public string Title { get; init; } = "Game";

    /// <summary>Virtual resolution; SDL letterboxes it onto the real display.</summary>
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;

    public Color ClearColor { get; init; } = new(0.1f, 0.1f, 0.15f);

    /// <summary>Allowed screen orientations on phones and tablets.</summary>
    public Orientation Orientation { get; init; } = Orientation.Landscape;

    /// <summary>Overrides the platform default content location (must end with a separator).</summary>
    public string? ContentRoot { get; init; }
}
