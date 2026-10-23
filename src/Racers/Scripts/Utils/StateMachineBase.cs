using System.Text;
using Engine;

namespace Racers;

public abstract class StateMachineBase< TState > where TState : StateBase< TState >
{
    public bool logDebug { get; set; }

    public TState? currentState => stack.Count > 0 ? stack.Peek() : null;

    readonly Stack< TState > stack = new();
    readonly StringBuilder debugSb = new();

    public void Set< T > () where T : TState, new()
        => Set( new T() );

    public void Set ( TState state )
    {
        var previous = currentState;
        ExitAll( state );

        stack.Push( state );
        LogDebug( $"Entering { Name( state ) } via Set" );
        state.Enter( previous );
    }

    public void Push< T > () where T : TState, new()
        => Push( new T() );

    public void Push ( TState state )
    {
        var previous = currentState;

        if ( previous != null )
        {
            LogDebug( $"Unfocusing { Name( previous ) } via Push" );
            previous.Unfocus( state );
        }

        stack.Push( state );
        LogDebug( $"Entering { Name( state ) } via Push" );
        state.Enter( previous );
    }

    public void Pop ()
    {
        if ( stack.Count == 0 )
        {
            Log.Error( "Trying to pop state, but the stack is empty." );
            return;
        }

        var popped = stack.Pop();
        var next = currentState;

        LogDebug( $"Exiting { Name( popped ) } via Pop" );
        popped.Exit( next );

        if ( next != null )
        {
            LogDebug( $"Focusing { Name( next ) } via Pop" );
            next.Focus( popped );
        }
    }

    public void Clear ()
        => ExitAll( null );

    public bool IsInStack< T > ()
        => stack.Any( state => state is T );

    public bool Is< T > ()
        => currentState != null && currentState is T;

    public string GetStack ()
    {
        debugSb.Clear();
        debugSb.Append( "CURRENT STACK: " );

        if ( stack.Count == 0 )
            debugSb.Append( "[Empty]" );

        foreach ( var state in stack.Reverse() )
        {
            debugSb.Append( Name( state ) );
            debugSb.Append( '/' );
        }

        return debugSb.ToString();
    }

    void ExitAll ( TState? next )
    {
        while ( stack.Count > 0 )
        {
            var popped = stack.Pop();
            LogDebug( $"Exiting { Name( popped ) } via Clear" );
            popped.Exit( next );
        }
    }

    void LogDebug ( string message )
    {
        if ( logDebug )
            Log.Info( $"{ message }\n{ GetStack() }" );
    }

    static string Name ( TState state ) => state.GetType().Name;
}
