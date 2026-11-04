using Engine;
using MintECS;
using System.Collections;
using System.Collections.Generic;
using Vo;

namespace Racers;

public class UnitViewSystem : WorldManipulator, IViewSystem
{
    const string UNIT_INFO_DATA = "UI_UnitInfo";
    const string HEALTH_RESOURCE = "Health";
    const string SPEED_RESOURCE = "Speed";

    const int INFO_SORT_ORDER = 100;
    const float INFO_Y_OFFSET = -6f;

    Filter visibleUnits;
    Filter unitsOnCanvas;
    Filter infos;

    Dictionary< uint, uint > infoByUnit = new();

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

        unitsOnCanvas = new FilterBuilder()
            .Include< Unit >()
            .Include< Position >()
            .Include< Rect >()
            .Include< OnCanvas >()
            .ToFilter();

        Start( unitsOnCanvas );

        infos = new FilterBuilder()
            .Include< UnitInfo >()
            .Include< Position >()
            .ToFilter();

        infoByUnit.Clear();

        infos.onAdd += OnAddInfo;
        infos.onRemove += OnRemoveInfo;

        Start( infos );
    }

    public void StopSystem ( World world )
    {
        Stop( infos );

        infos.onAdd -= OnAddInfo;
        infos.onRemove -= OnRemoveInfo;

        infoByUnit.Clear();

        Stop( unitsOnCanvas );

        Stop( visibleUnits );

        visibleUnits.onAdd -= OnAddVisibleUnit;
        visibleUnits.onRemove -= OnRemoveVisibleUnit;

        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        var grid = Get< GridViewSystem >();

        // foreach ( var unit in visibleUnits )
        // {
        //     world.Get< Position >( unit ).offset.y += Mathf.Lerp( -.2f, .2f, VoHelp.SinWave( t * 50f ) );    
        // }
        

        var moveMessages = Read< UnitMoved >();
        foreach ( var moveMessage in moveMessages )
        {
        	var unitRect = Get< Rect >( moveMessage.unit );
        	var startPosition = Get< Position >( moveMessage.unit ).value;
        	var endPosition = grid.GetCellCenter( moveMessage.to, ( int )unitRect.width, ( int )unitRect.height );

            float moveTime = 0.9f;
            Get< CutsceneSystem >().StartCutscene( $"Move { GetName( moveMessage.unit ) }",
                MoveUnitCo( moveMessage.unit, startPosition, endPosition, moveTime ),
                moveMessage.unit
            );
        }

        foreach ( var unit in unitsOnCanvas.entities )
        {
            if ( !infoByUnit.ContainsKey( unit ) )
                CreateInfo( unit );
        }

        foreach ( var info in infos.GetListCopy() )
        {
            var unit = Get< UnitInfo >( info ).relation;
            if ( !IsLiveUnit( unit ) )
                DestroyInfo( info );
        }
    }

    void CreateInfo ( uint unit )
    {
        var info = Get< PrefabViewSystem >().CreateFromData( UNIT_INFO_DATA, $"{ GetBasicName( unit ) } Info", GetRelated< OnCanvas >( unit ) );
        Get< Rect >( info ).pivot = new Vector2( 0.5f, 0f );
        Add( info, new SortOrder { value = INFO_SORT_ORDER } );
        SetRelation< UnitInfo >( info, unit );

        var unitRect = Get< Rect >( unit );
        var unitPosition = Get< Position >( unit ).value;
        Get< Position >( info ).value = new Vector2(
            unitPosition.x + ( unitRect.width * ( 0.5f - unitRect.pivot.x ) ),
            unitPosition.y - ( unitRect.height * unitRect.pivot.y ) + INFO_Y_OFFSET
        );

        Get< TransformSystem >().SetParent( info, unit );

        var viewSystem = Get< ResourceViewSystem >();
        viewSystem.Connect( info, unit, HEALTH_RESOURCE );
        viewSystem.Connect( info, unit, SPEED_RESOURCE );
    }

    void DestroyInfo ( uint info )
    {
        foreach ( var resourceView in GetEntities< ResourceView >( info ) )
            Destroy( resourceView );

        Destroy( info );
    }

    bool IsLiveUnit ( uint unit )
        => Exists( unit ) && Has< Unit >( unit ) && Has< Position >( unit ) && Has< Rect >( unit ) && Has< OnCanvas >( unit );

    void OnAddInfo ( uint info )
        => infoByUnit[ Get< UnitInfo >( info ).relation ] = info;

    void OnRemoveInfo ( uint info )
    {
        foreach ( var ( unit, shownInfo ) in infoByUnit )
        {
            if ( shownInfo == info )
            {
                infoByUnit.Remove( unit );
                return;
            }
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
