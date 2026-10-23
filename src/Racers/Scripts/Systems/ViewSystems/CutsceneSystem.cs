using Engine;
using MintECS;
using Racers.Coroutines;
using System.Collections;
using System.Collections.Generic;
using Vo;

namespace Racers;

public class CutsceneSystem : WorldManipulator, IViewSystem
{
    Dictionary< uint, Coroutine > cutscenes;

    List< uint > pendingRemovals = new();

    public void StartSystem ( World world )
    {
        this.world = world;
        cutscenes = new();
    }

    public void StopSystem ( World world )
    {
        this.world = null;
        cutscenes = null;
    }

    public void Update ( World world, FrameTime time )
    {
    	pendingRemovals.Clear();
    	foreach ( var kvp in cutscenes )
    	{
    		if ( !kvp.Value.isCompleted )
    			continue;

    		pendingRemovals.Add( kvp.Key );
    	}

    	foreach ( var finishedCutscene in pendingRemovals )
    	{
    		var entities = GetEntities< InCutscene >( finishedCutscene );
    		foreach ( var entity in entities )
    			Remove< InCutscene >( entity );

    		cutscenes.Remove( finishedCutscene );
    		Destroy( finishedCutscene );
    	}

        var uiState = Get< GameViewSystem >().uiState;
        if ( cutscenes.Count == 0 && uiState.Is< Race_CutsceneState >() )
            uiState.Pop();

    }
 
 	public void StartCutscene ( string name, IEnumerator enumerator, params uint[] entities )
 	{
 		var coroutines = Get< CoroutineSystem >();
 		
 		var cutscene = Create( name );

 		foreach ( var entity in entities )
 		{
 			if ( Has< InCutscene >( entity ) )
 			{
 				var otherCutscene = GetRelated< InCutscene >( entity );
 				Log.Warning( $"{ GetName( entity ) } is trying to be added to a cutscene { GetName( cutscene ) }, but already has { GetName( otherCutscene ) }! Ignoring." );
 				continue;
 			}

 			SetRelation< InCutscene >( entity, cutscene );
 		}

 		cutscenes[ cutscene ] = coroutines.Start( enumerator, Time.Game );

        var uiState = Get< GameViewSystem >().uiState;
        if ( !uiState.IsInStack< Race_CutsceneState >() )
            uiState.Push< Race_CutsceneState >();
 	}
}
