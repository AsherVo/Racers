using Engine;
using MintECS;
using Racers;

using System.Collections;
using System.Collections.Generic;

namespace Racers.Coroutines;

public class CoroutineSystem : WorldManipulator, ISystem
{
    List< Coroutine > coroutines = new List< Coroutine >();

    public void StartSystem ( World world ) 
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {
        this.world = null;
    }

    public void TickSystem ( World world )
    {   
        var timePassed = ReadSingle< TimePassed >();
        if ( timePassed == null )
            return;

        float dt = timePassed.dt;
        for ( int i = 0; i < coroutines.Count; i++ )
        {
            if ( !coroutines[i].MoveNext( dt ) )
            {
                coroutines.RemoveAt( i );
                i--;
            }
        }
    }

    public Coroutine Start ( IEnumerator enumerator, Time timeType )
    {
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