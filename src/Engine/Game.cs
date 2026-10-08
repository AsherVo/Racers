using System.Numerics;

namespace Engine;

/// <summary>Base class for a game. The engine calls these hooks; hosts never touch them directly.</summary>
public abstract class Game
{
    public Graphics Graphics { get; internal set; } = null!;
    public ContentManager Content { get; internal set; } = null!;

    internal bool ExitRequested { get; private set; }

    public void Exit () => ExitRequested = true;

    protected internal virtual void Load () { }
    protected internal virtual void Update ( GameTime time ) { }
    protected internal virtual void Draw ( SpriteBatch batch ) { }
    protected internal virtual void Unload () { }

    /// <summary>Mouse and touch, in game pixels with (0, 0) at the bottom-left.</summary>
    protected internal virtual void OnPointer ( PointerEvent e ) { }
    protected internal virtual void OnKey ( Key key, bool down ) { }

    /// <summary>The window changed size: <see cref="Graphics.PixelScale"/> may differ, and with a dynamic size, so may the resolution.</summary>
    protected internal virtual void OnResize () { }
}

public readonly record struct GameTime ( double TotalSeconds, float DeltaSeconds );

public enum PointerAction { Down, Move, Up }

public readonly record struct PointerEvent ( PointerAction Action, Vector2 Position );

public enum Orientation { Landscape, Portrait, Any }

public sealed record GameOptions
{
    /// <summary>Content file holding the <see cref="GameConfig"/>. Null uses <see cref="GameConfig"/>'s defaults.</summary>
    public string? ConfigPath { get; init; } = "config.yaml";

    public Color ClearColor { get; init; } = new( 0.1f, 0.1f, 0.15f );

    /// <summary>Allowed screen orientations on phones and tablets.</summary>
    public Orientation Orientation { get; init; } = Orientation.Landscape;

    /// <summary>Overrides the platform default content location (must end with a separator).</summary>
    public string? ContentRoot { get; init; }
}
