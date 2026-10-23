using Engine;
using MintECS;
using System.Collections;
using System.Collections.Generic;
using Vo;

namespace Racers;

public class UnitViewSystem : WorldManipulator, IViewSystem
{
    Filter units;

    public void StartSystem ( World world )
    {
        this.world = world;

        units = new FilterBuilder()
            .Include< Unit >()
            .Include< InCell >()
            .Include< Position >()
            .Exclude< InCutscene >()
            .ToFilter();

        Start( units );
    }

    public void StopSystem ( World world )
    {
        Stop( units );

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

        foreach ( var unit in units.entities )
        {
        	var coords = Get< Cell >( GetRelated< InCell >( unit ) ).coords;
        	var rect = Get< Rect >( unit );
        	Get< Position >( unit ).value = grid.CenterOnCell( coords, ( int )rect.width, ( int )rect.height );
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
    }
}
