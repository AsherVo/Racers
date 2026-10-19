using Engine;
using MintECS;
using Racers;

using System.Collections;
using System.Collections.Generic;

namespace Racers.Coroutines;

public class CoroutineSystem : WorldManipulator, IViewSystem
{
    List< Coroutine > coroutines = new List< Coroutine >();

    public void StartSystem ( World world ) 
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {
        coroutines.Clear();
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {   
        var timeSystem = Get< TimeSystem >();
        for ( int i = 0; i < coroutines.Count; i++ )
        {
            float dt = timeSystem.GetDelta( coroutines[i].timeType );
            if ( dt == 0f )
                continue;

            if ( !coroutines[i].MoveNext( dt ) )
            {
                coroutines.RemoveAt( i );
                i--;
            }
        }
    }

    public Coroutine Start ( IEnumerator enumerator, Time timeType )
    {
        if ( timeType == Time.None )
            throw new ArgumentException( "A coroutine needs a time type to follow.", nameof( timeType ) );

        var coroutine = new Coroutine( enumerator, timeType );
        
        if ( coroutine.MoveNext( 0f ) )
            coroutines.Add( coroutine );

        return coroutine;
    }

    public void Stop ( Coroutine coroutine )
    {
        coroutines.Remove(coroutine);
    }
}