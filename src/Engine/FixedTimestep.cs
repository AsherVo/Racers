namespace Engine;

/// <summary>
/// Turns real elapsed time into whole fixed ticks. Time is kept in integer nanoseconds, so the number of
/// ticks never depends on float rounding.
/// </summary>
public sealed class FixedTimestep
{
    public const ulong NANOSECONDS_PER_SECOND = 1_000_000_000;

    public ulong tickNs { get; }
    public int maxTicksPerAdvance { get; }

    /// <summary>Time carried over toward the next tick, always less than <see cref="tickNs"/> after <see cref="Advance"/>.</summary>
    public ulong accumulator { get; private set; }

    /// <summary>How far the next tick is from happening, in [0, 1). Views interpolate with it.</summary>
    public float alpha => accumulator / ( float )tickNs;

    /// <exception cref="ArgumentOutOfRangeException">A tick would be shorter than a nanosecond, or the cap isn't positive.</exception>
    public FixedTimestep ( int ticksPerSecond, int maxTicksPerAdvance )
    {
        if ( ticksPerSecond <= 0 || ( ulong )ticksPerSecond > NANOSECONDS_PER_SECOND )
            throw new ArgumentOutOfRangeException( nameof( ticksPerSecond ), ticksPerSecond, "Must be between 1 and 1,000,000,000." );
        if ( maxTicksPerAdvance <= 0 )
            throw new ArgumentOutOfRangeException( nameof( maxTicksPerAdvance ), maxTicksPerAdvance, "Must be positive." );

        tickNs = NANOSECONDS_PER_SECOND / ( ulong )ticksPerSecond;
        this.maxTicksPerAdvance = maxTicksPerAdvance;
    }

    /// <summary>
    /// Adds elapsed time and returns how many ticks to run now. Past <see cref="maxTicksPerAdvance"/>, the excess time is
    /// dropped instead of being owed as ticks, so a slow machine falls behind real time instead of spiraling.
    /// </summary>
    public int Advance ( ulong elapsedNs )
    {
        accumulator = elapsedNs > ulong.MaxValue - accumulator ? ulong.MaxValue : accumulator + elapsedNs;

        ulong due = accumulator / tickNs;
        int ticks = due > ( ulong )maxTicksPerAdvance ? maxTicksPerAdvance : ( int )due;

        accumulator = ticks < maxTicksPerAdvance ? accumulator - ( ulong )ticks * tickNs : accumulator % tickNs;
        return ticks;
    }

    public void Reset () => accumulator = 0;
}
