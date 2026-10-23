using Engine;
using MintECS;
using System.Collections;
using System.Collections.Generic;
using Vo;

namespace Racers;

public class UnitViewSystem : WorldManipulator, IViewSystem
{
    Filter visibleUnits;

    public void StartSystem ( World world )
    {
        this.world = world;

        visibleUnits = new FilterBuilder()
            .Include< Unit >()
            .Include< Position >()
            .ToFilter();

        visibleUnits.onAdd += OnAddVisibleUnit;
        visibleUnits.onRemove += OnRemoveVisibleUnit;

        Start( visibleUnits );
    }

    public void StopSystem ( World world )
    {
        Stop( visibleUnits );

        visibleUnits.onAdd -= OnAddVisibleUnit;
        visibleUnits.onRemove -= OnRemoveVisibleUnit;

        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        var grid = Get< GridViewSystem >();

        var moveMessages = Read< UnitMoved >();
        foreach ( var moveMessage in moveMessages )
        {
        	var unitRect = Get< Rect >( moveMessage.unit );
        	var startPosition = Get< Position >( moveMessage.unit ).value;
        	var endPosition = grid.CenterOnCell( moveMessage.to, ( int )unitRect.width, ( int )unitRect.height );

            float moveTime = 0.9f;
            Get< CutsceneSystem >().StartCutscene( $"Move { GetName( moveMessage.unit ) }",
                MoveUnitCo( moveMessage.unit, startPosition, endPosition, moveTime ),
                moveMessage.unit
            );
        }
    }
 
    IEnumerator MoveUnitCo ( uint unit, Vector2 startPosition, Vector2 endPosition, float moveTime )
    {
    	var timeSystem = Get< TimeSystem >();

    	float t = 0f;
    	while ( t < moveTime )
    	{
    		t += timeSystem.GetDelta( Time.Game );
    		float p = Ease.InOut( Float.Clamp01( t / moveTime ) );
    		Get< Position >( unit ).value = Vector2.Lerp( startPosition, endPosition, p );
    		yield return null;
    	}

        if ( Has< InCell >( unit ) )
            Get< GridViewSystem >().SnapToCell( unit );
    }

    void OnAddVisibleUnit ( uint unit )
    {
        var canvas = Get< GameViewSystem >().raceCanvas;

        if ( canvas != 0 )
            SetRelation< OnCanvas >( unit, canvas );

        if ( Has< InCell >( unit ) )
            Get< GridViewSystem >().SnapToCell( unit );
    }

    void OnRemoveVisibleUnit ( uint unit )
    {
        Remove< OnCanvas >( unit );
    }

    public void Refresh ()
    {
        foreach ( var unit in visibleUnits.entities )
        {
            OnRemoveVisibleUnit( unit );
            OnAddVisibleUnit( unit );
        }
    }
}
