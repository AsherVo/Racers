namespace Engine.Tests;

public class FixedTimestepTests
{
    const ulong MS = 1_000_000;

    [Fact]
    public void TickLengthIsWholeNanoseconds ()
    {
        Assert.Equal( 16_666_666UL, new FixedTimestep( 60, 8 ).tickNs );
        Assert.Equal( 20_000_000UL, new FixedTimestep( 50, 8 ).tickNs );
    }

    [Fact]
    public void RunsWholeTicksAndCarriesTheRest ()
    {
        var timestep = new FixedTimestep( 50, 8 );

        Assert.Equal( 0, timestep.Advance( 15 * MS ) );
        Assert.Equal( 15 * MS, timestep.accumulator );
        Assert.Equal( 0.75f, timestep.alpha );

        Assert.Equal( 1, timestep.Advance( 10 * MS ) );
        Assert.Equal( 5 * MS, timestep.accumulator );

        Assert.Equal( 2, timestep.Advance( 40 * MS ) );
        Assert.Equal( 5 * MS, timestep.accumulator );
    }

    [Fact]
    public void FrameRateDoesNotChangeTheTickCount ()
    {
        var at60 = new FixedTimestep( 60, 8 );
        var at144 = new FixedTimestep( 60, 8 );
        long ticksAt60 = 0;
        long ticksAt144 = 0;

        for ( int i = 0; i < 60 * 10; i++ )
            ticksAt60 += at60.Advance( FixedTimestep.NANOSECONDS_PER_SECOND / 60 );
        for ( int i = 0; i < 144 * 10; i++ )
            ticksAt144 += at144.Advance( FixedTimestep.NANOSECONDS_PER_SECOND / 144 );

        Assert.InRange( ticksAt60, 599, 600 );
        Assert.InRange( ticksAt144, 599, 600 );
    }

    [Fact]
    public void CapDropsExcessTimeInsteadOfOwingTicks ()
    {
        var timestep = new FixedTimestep( 50, 3 );

        Assert.Equal( 3, timestep.Advance( 1000 * MS + 7 * MS ) );
        Assert.Equal( 7 * MS, timestep.accumulator );

        Assert.Equal( 0, timestep.Advance( 1 * MS ) );
    }

    [Fact]
    public void ExactlyTheCapKeepsTheRemainder ()
    {
        var timestep = new FixedTimestep( 50, 3 );

        Assert.Equal( 3, timestep.Advance( 63 * MS ) );
        Assert.Equal( 3 * MS, timestep.accumulator );
    }

    [Fact]
    public void ResetClearsCarriedTime ()
    {
        var timestep = new FixedTimestep( 50, 8 );
        timestep.Advance( 19 * MS );

        timestep.Reset();

        Assert.Equal( 0UL, timestep.accumulator );
        Assert.Equal( 0, timestep.Advance( 1 * MS ) );
    }

    [Fact]
    public void HugeElapsedTimeSaturatesInsteadOfWrapping ()
    {
        var timestep = new FixedTimestep( 50, 4 );
        timestep.Advance( 19 * MS );

        Assert.Equal( 4, timestep.Advance( ulong.MaxValue ) );
        Assert.True( timestep.accumulator < timestep.tickNs );
    }

    [Theory]
    [InlineData( 0, 8 )]
    [InlineData( -1, 8 )]
    [InlineData( 60, 0 )]
    public void RejectsInvalidSettings ( int ticksPerSecond, int maxTicksPerAdvance )
    {
        Assert.Throws< ArgumentOutOfRangeException >( () => new FixedTimestep( ticksPerSecond, maxTicksPerAdvance ) );
    }
}
