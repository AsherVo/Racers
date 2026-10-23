using Engine;
using MintECS;
using Racers.Coroutines;
using System.Collections;
using System.Collections.Generic;
using Vo;

namespace Racers;

public class UISystem : StateMachineManipulator< UIState >, IViewSystem
{
	World world;

    public void StartSystem ( World world )
    {
        this.world = world;
        this.stateMachine = new UIStateMachine();
        this.stateMachine.logDebug = true;
    }

    public void StopSystem ( World world )
    {
        this.world = null;
        this.stateMachine = null;
    }

    public void Update ( World world, FrameTime time )
    {
        
    }
}
