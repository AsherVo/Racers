namespace Engine;

/// <summary>Base class for a game. The engine calls these hooks; hosts never touch them directly.</summary>
public abstract class Game
{
    public Graphics graphics { get; internal set; } = null!;
    public ContentManager content { get; internal set; } = null!;

    /// <summary>How many times per second of app time <see cref="FixedUpdate"/> runs, from <see cref="GameConfig.ticksPerSecond"/>.</summary>
    public int ticksPerSecond { get; internal set; }

    internal bool exitRequested { get; private set; }

    public void Exit () => exitRequested = true;

    protected internal virtual void Load () { }

    /// <summary>One fixed simulation tick. Runs 0..N times per frame, before <see cref="Update"/>, never with real time.</summary>
    protected internal virtual void FixedUpdate () { }

    /// <summary>Once per frame, after the frame's fixed ticks.</summary>
    protected internal virtual void Update ( FrameTime time ) { }
    protected internal virtual void Draw ( SpriteBatch batch ) { }
    protected internal virtual void Unload () { }

    /// <summary>Mouse and touch, in game pixels with (0, 0) at the bottom-left.</summary>
    protected internal virtual void OnPointer ( PointerEvent e ) { }
    protected internal virtual void OnKey ( Key key, bool down ) { }

    /// <summary>The window changed size: <see cref="Graphics.pixelScale"/> may differ, and with a dynamic size, so may the resolution.</summary>
    protected internal virtual void OnResize () { }
}

/// <param name="realDt">Seconds since the last frame, unclamped. Includes time spent in the background.</param>
/// <param name="appDt">Seconds since the last frame, clamped to <see cref="GameRunner.MAX_FRAME_NS"/>. Excludes time spent in the background.</param>
/// <param name="alpha">How far the next fixed tick is from happening, in [0, 1). Views interpolate with it.</param>
public readonly record struct FrameTime ( float realDt, float appDt, float alpha );

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
