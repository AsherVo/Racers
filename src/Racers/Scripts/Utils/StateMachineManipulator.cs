using System.Text;
using Engine;

namespace Racers;

public class StateMachineManipulator< TState > where TState : StateBase< TState >
{
    protected StateMachineBase< TState > stateMachine;

    public void Set< T > () where T : TState, new()
        => stateMachine.Set< T >();

    public void Set ( TState state )
        => stateMachine.Set( state );

    public void Push< T > () where T : TState, new()
        => stateMachine.Push< T >();

    public void Push ( TState state )
        => stateMachine.Push( state );

    public void Pop ()
        => stateMachine.Pop();

    public void Clear ()
        => stateMachine.Clear();

    public bool IsInStack< T > ()
        => stateMachine.IsInStack< T > ();

    public bool InState< T > ()
        => stateMachine.InState< T >();
}
