using Engine;

namespace MintECS;

/// <summary>
/// View time. A utility the world feeds from <see cref="World.Update"/>: each frame it advances one <see cref="Clock"/>
/// entity per <see cref="Time"/> type, which it creates lazily in the view phase, so clocks never take sim ids. The sim
/// never reads it; sim time is <see cref="World.tick"/>.
/// </summary>
public class TimeSystem : WorldManipulator, ISystem
{
    static readonly Time[] ClockTypes = { Time.Realtime, Time.App, Time.Game };

    readonly uint[] clocks = new uint[ ClockTypes.Length + 1 ];

    public void StartSystem ( World world )
    {
        this.world = world;
        Array.Clear( clocks );

        var found = new List< uint >();
        GetEntitiesNonAlloc( new FilterBuilder().Include< Clock >(), found );
        found.Sort();

        foreach ( var entity in found )
        {
            var type = Get< Clock >( entity ).type;
            if ( !IsClockType( type ) )
                Log.Warning( $"{ GetName( entity ) } is a clock with type { type }. Ignoring it." );
            else if ( clocks[ ( int )type ] != 0 )
                Log.Warning( $"{ GetName( entity ) } is a second { type } clock. Ignoring it." );
            else
                clocks[ ( int )type ] = entity;
        }
    }

    public void StopSystem ( World world )
    {
        Array.Clear( clocks );
        this.world = null;
    }

    /// <summary>Called by <see cref="World.Update"/> before the view systems run.</summary>
    public void AdvanceFrame ( FrameTime time )
    {
        foreach ( var type in ClockTypes )
        {
            var clock = GetOrCreateClock( type );
            float delta = type == Time.Realtime ? time.realDt : time.appDt;

            clock.delta = delta;
            clock.total += delta;
            clock.deltaTicks = ( int )Math.Clamp( world.tick - clock.lastTick, 0, int.MaxValue );
            clock.lastTick = world.tick;
        }
    }

    /// <summary>Seconds of <paramref name="type"/> time this frame.</summary>
    public float GetDelta ( Time type )
        => GetClock( type )?.delta ?? 0f;

    /// <summary>Seconds of <paramref name="type"/> time since its clock was created.</summary>
    public double GetTotal ( Time type )
        => GetClock( type )?.total ?? 0;

    /// <summary>How many fixed ticks ran since the last frame.</summary>
    public int ticksThisFrame
        => GetClock( Time.Realtime )?.deltaTicks ?? 0;

    Clock? GetClock ( Time type )
    {
        if ( world.phase == WorldPhase.Sim )
            throw new InvalidOperationException( "The sim read view time. Sim time is world.tick." );

        if ( !IsClockType( type ) )
            throw new ArgumentOutOfRangeException( nameof( type ), type, "Not a clock type." );

        uint entity = clocks[ ( int )type ];
        return entity != 0 && Exists( entity ) ? Get< Clock >( entity ) : null;
    }

    Clock GetOrCreateClock ( Time type )
    {
        uint entity = clocks[ ( int )type ];
        if ( entity != 0 && Exists( entity ) && TryGet( entity, out Clock clock ) )
            return clock;

        clock = new Clock { type = type };
        clocks[ ( int )type ] = Create( $"Clock ({ type })", clock );
        return clock;
    }

    static bool IsClockType ( Time type )
        => Array.IndexOf( ClockTypes, type ) >= 0;
}
