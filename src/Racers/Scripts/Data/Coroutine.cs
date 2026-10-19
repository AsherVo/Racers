using MintECS;
using Racers;
using System.Collections;
using System.Collections.Generic;

namespace Racers.Coroutines;

public class Coroutine
{
    public Time timeType { get; private set; }

    public IEnumerator enumerator { get; private set; }
    public bool isRunning { get; private set; }
    public bool isCompleted { get; private set; }
    private object current;

    public Coroutine ( IEnumerator enumerator, Time timeType )
    {
        this.enumerator = enumerator;
        this.timeType = timeType;
        isRunning = false;
        isCompleted = false;
    }

    public bool MoveNext( float dt )
    {
        if ( isCompleted )
            return false;

        if ( current is WaitForSeconds waitForSeconds )
        {
            if ( waitForSeconds.KeepWaiting( dt ) )
                return true;
        }

        isRunning = true;
        if ( !enumerator.MoveNext() )
        {
            isCompleted = true;
            isRunning = false;
            return false;
        }

        current = enumerator.Current;
        return true;
    }
}

public class WaitForSeconds
{
    float timeLeft;

    public WaitForSeconds ( float seconds )
    {
        timeLeft = seconds;
    }

    public bool KeepWaiting ( float dt )
    {
        timeLeft -= dt;
        return timeLeft >= 0f;
    }
}