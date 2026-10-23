using System.Linq;

using Engine;
using MintECS;

namespace Racers;

public class RaceSystem : WorldManipulator, ISimSystem
{
    Filter units;

    public void StartSystem ( World world )
    {
        this.world = world;

        units = new FilterBuilder()
            .Include< Unit >()
            .Include< InCell >()
            .ToFilter();

        Start( units );
    }

    public void StopSystem ( World world )
    {
        Stop( units );

        this.world = null; 
    }

    public void Setup ( RaceConfig race )
    {
        var grid = Get< GridSystem >();
        grid.SpawnGrid( race.width, race.height );

        foreach ( var unit in race.units )
        {
            var entity = Get< PrefabSystem >().CreateFromData( unit.id );
            var coords = new Vector2Int( unit.x, unit.y );
            world.SetRelation< InCell >( entity, grid.GetCell( coords ) );    
        }
    }

    public void FixedUpdate ( World world )
    {
        var grid = Get< GridSystem >();

        foreach ( var select in Read< SelectCell >() )
        {
            var coords = new Vector2Int( select.x, select.y );
            if ( !grid.InBounds( coords ) )
                continue;

            var cell = grid.GetCell( coords );
            Log.Info( $"[Sim] Tick { select.tick }: selected { GetName( cell ) }" );

            var unit = units.entities.FirstOrDefault();
            MoveUnit( unit, coords );
        }
    }

    public void MoveUnit ( uint unit, Vector2Int coords )
    {
        var startCell = GetRelated< InCell >( unit );
        var startCoords = Get< Cell >( startCell ).coords;

        var grid = Get< GridSystem >();
        var cell = grid.GetCell( coords );
        SetRelation< InCell >( unit, cell );

        Send( new UnitMoved {
            unit = unit,
            from = startCoords,
            to = coords
        } );
    }
}
