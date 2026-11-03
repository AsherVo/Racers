using Engine;
using MintECS;

namespace Racers;

public class UnitInfoViewSystem : WorldManipulator, IViewSystem
{
    const string UNIT_INFO_DATA = "UI_UnitInfo";
    const string HEALTH_RESOURCE = "Health";
    const string SPEED_RESOURCE = "Speed";
    const string RIVE_HEALTH = "health";
    const string RIVE_SPEED = "speed";

    const int INFO_SORT_ORDER = 100;
    const float INFO_Y_OFFSET = -6f;

    Filter units = new FilterBuilder()
        .Include< Unit >()
        .Include< Position >()
        .Include< Rect >()
        .Include< OnCanvas >()
        .ToFilter();

    Filter infos = new FilterBuilder()
        .Include< UnitInfo >()
        .Include< Position >()
        .ToFilter();

    Dictionary< uint, uint > infoByUnit = new();
    Dictionary< uint, ( RiveInstance rive, int health, int speed ) > shownValues = new();

    public void StartSystem ( World world )
    {
        this.world = world;

        infoByUnit.Clear();
        shownValues.Clear();

        Start( units );

        infos.onAdd += OnAddInfo;
        infos.onRemove += OnRemoveInfo;
        Start( infos );
    }

    public void StopSystem ( World world )
    {
        Stop( infos );
        infos.onAdd -= OnAddInfo;
        infos.onRemove -= OnRemoveInfo;

        Stop( units );

        infoByUnit.Clear();
        shownValues.Clear();

        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        foreach ( var unit in units.entities )
        {
            if ( !infoByUnit.ContainsKey( unit ) )
                CreateInfo( unit );
        }

        foreach ( var info in infos.GetListCopy() )
        {
            var unit = Get< UnitInfo >( info ).relation;
            if ( !IsLiveUnit( unit ) )
            {
                Destroy( info );
                continue;
            }

            FollowUnit( info, unit );
            ShowValues( info, unit );
        }
    }

    void CreateInfo ( uint unit )
    {
        var info = Get< PrefabViewSystem >().CreateFromData( UNIT_INFO_DATA, $"{ GetName( unit ) } Info", GetRelated< OnCanvas >( unit ) );
        Get< Rect >( info ).pivot = new Vector2( 0.5f, 0f );
        Add( info, new SortOrder { value = INFO_SORT_ORDER } );
        SetRelation< UnitInfo >( info, unit );
    }

    bool IsLiveUnit ( uint unit )
        => Exists( unit ) && Has< Unit >( unit ) && Has< Position >( unit ) && Has< Rect >( unit ) && Has< OnCanvas >( unit );

    void FollowUnit ( uint info, uint unit )
    {
        var unitRect = Get< Rect >( unit );
        var unitPosition = Get< Position >( unit ).value;

        Get< Position >( info ).value = new Vector2(
            unitPosition.x + ( unitRect.width * ( 0.5f - unitRect.pivot.x ) ),
            unitPosition.y - ( unitRect.height * unitRect.pivot.y ) + INFO_Y_OFFSET
        );
    }

    void ShowValues ( uint info, uint unit )
    {
        var rive = Get< RenderSystem >().GetRive( info );
        if ( rive == null )
            return;

        var resources = Get< ResourceSystem >();
        var health = resources.GetValue( unit, HEALTH_RESOURCE );
        var speed = resources.GetValue( unit, SPEED_RESOURCE );

        if ( shownValues.TryGetValue( info, out var shown ) && shown.rive == rive && shown.health == health && shown.speed == speed )
            return;

        rive.SetViewModelString( RIVE_HEALTH, health.ToString() );
        rive.SetViewModelString( RIVE_SPEED, speed.ToString() );
        shownValues[ info ] = ( rive, health, speed );
    }

    void OnAddInfo ( uint info )
        => infoByUnit[ Get< UnitInfo >( info ).relation ] = info;

    void OnRemoveInfo ( uint info )
    {
        shownValues.Remove( info );

        foreach ( var ( unit, shownInfo ) in infoByUnit )
        {
            if ( shownInfo == info )
            {
                infoByUnit.Remove( unit );
                return;
            }
        }
    }
}
