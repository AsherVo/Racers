namespace Racers;

public abstract class StateBase< TState > where TState : StateBase< TState >
{
    public virtual void Enter ( TState? previous ) {}
    public virtual void Exit ( TState? next ) {}
    public virtual void Focus ( TState previous ) {}
    public virtual void Unfocus ( TState next ) {}
}
