using System.Numerics;

namespace Engine;

/// <summary>Base class for a game. The engine calls these hooks; hosts never touch them directly.</summary>
public abstract class Game
{
    public Graphics graphics { get; internal set; } = null!;
    public ContentManager content { get; internal set; } = null!;

    internal bool exitRequested { get; private set; }

    public void Exit () => exitRequested = true;

    protected internal virtual void Load () { }
    protected internal virtual void Update ( GameTime time ) { }
    protected internal virtual void Draw ( SpriteBatch batch ) { }
    protected internal virtual void Unload () { }

    /// <summary>Mouse and touch, in game pixels with (0, 0) at the bottom-left.</summary>
    protected internal virtual void OnPointer ( PointerEvent e ) { }
    protected internal virtual void OnKey ( Key key, bool down ) { }

    /// <summary>The window changed size: <see cref="Graphics.pixelScale"/> may differ, and with a dynamic size, so may the resolution.</summary>
    protected internal virtual void OnResize () { }
}

public readonly record struct GameTime ( double totalSeconds, float deltaSeconds );

public enum PointerAction { Down, Move, Up }

public readonly record struct PointerEvent ( PointerAction action, Vector2 position );

public enum Orientation { Landscape, Portrait, Any }

public sealed record GameOptions
{
    /// <summary>Content file holding the <see cref="GameConfig"/>. Null uses <see cref="GameConfig"/>'s defaults.</summary>
    public string? configPath { get; init; } = "config.yaml";

    public Color clearColor { get; init; } = new( 0.1f, 0.1f, 0.15f );

    /// <summary>Allowed screen orientations on phones and tablets.</summary>
    public Orientation orientation { get; init; } = Orientation.Landscape;

    /// <summary>Overrides the platform default content location (must end with a separator).</summary>
    public string? contentRoot { get; init; }
}
