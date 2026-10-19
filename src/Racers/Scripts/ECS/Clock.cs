namespace MintECS;

/// <summary>View time of one <see cref="Time"/> type. <see cref="TimeSystem"/> creates one per type and advances it every frame.</summary>
public class Clock : Component
{
	public Time type;

	/// <summary>Seconds since the clock was created. Saved with the world, so it survives a load.</summary>
	public double total;

	/// <summary>Seconds this frame.</summary>
	public float delta;

	/// <summary><see cref="World.tick"/> at the last frame.</summary>
	public long lastTick;

	/// <summary>How many fixed ticks ran since the last frame.</summary>
	public int deltaTicks;
}
