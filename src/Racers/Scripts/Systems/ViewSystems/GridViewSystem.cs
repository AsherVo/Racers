using Engine;
using MintECS;

namespace Racers;

public class GridViewSystem : WorldManipulator, IViewSystem
{
    const int gridStartX = 10;
    const int gridStartY = 2;
    const int yOffset = 6;
    const int gridIncrementX = 40;
    const int gridIncrementY = 34;

    const int cellWidth = 40;
    const int cellHeight = 30;

    Dictionary< uint, uint > highlights = new();

    public void StartSystem ( World world )
    {
        this.world = world;

        highlights.Clear();
        foreach ( var ( highlight, cellHighlight ) in GetAllHighlights() )
            highlights[ cellHighlight.relation ] = highlight;
    }

    public void StopSystem ( World world )
    {
        highlights.Clear();
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        if ( !Get< GameViewSystem >().uiState.Is< Race_DefaultState >() )
            return;

        foreach ( var click in Read< Click >() )
        {
            if ( Exists( click.target ) && TryGet( click.target, out Cell cell ) )
                Send( new SelectCell { x = cell.coords.x, y = cell.coords.y } );
        }
    }

    public void Setup ( uint canvas )
    {
        var prefab = Get< PrefabSystem >();

        foreach ( var cell in Get< GridSystem >().GetAll() )
        {
            var coords = Get< Cell >( cell ).coords;

            prefab.AddFromData( cell, "Cell", canvas );
            Get< Position >( cell ).value = GetCellPosition( coords );
            Add( cell, new SortOrder { value = -1 } );

            // var highlight = world.Create( $"Cell ({ coords.x }, { coords.y }) Highlight", 
            //     new Position {},
            //     new CellHighlight {
            //         relation = cell,
            //     },
            //     new Sprite {},
            //     new SortOrder { 
            //         sortOrder = 1 
            //     }, 
            //     new SpriteShader {},
            //     new SpriteAnimation { 
            //         animation = "cell_select", 
            //         fps = 8 
            //     }
            // );
            // highlights[ cell ] = highlight;
        }
    }

    public uint GetHighlight ( uint cell )
        => highlights.TryGetValue( cell, out var highlight ) ? highlight : 0;

    public Vector2 GetCellPosition ( Vector2Int coords )
    {
        return new Vector2(
            gridStartX + ( yOffset * coords.y ) + ( gridIncrementX * coords.x ),
            gridStartY + ( gridIncrementY * coords.y )
        );
    }

    public Vector2 GetCellCenter ( Vector2Int coords, int width, int height )
    {
        var cellCenter = GetCellPosition( coords );
        cellCenter.x += cellWidth / 2;
        cellCenter.y += cellHeight / 2;

        var result = cellCenter;
        result.x -= width / 2;
        result.y -= height / 2;
        return result;
    }

    public Vector2 GetCellCenter ( Vector2Int coords, uint entity )
    {
        int width = 0;
        int height = 0;

        if ( TryGet< Rect >( entity, out Rect rect ) )
        {
            width = ( int )rect.width;
            height = ( int )rect.height;
        }

        return GetCellCenter( coords, width, height );
    }

    public void SnapToCell ( uint entity )
    {
        var onCell = GetRelated< InCell >( entity );
        if ( onCell == 0 )
        {
            Log.Warning( $"Trying to snap { GetName( entity ) } to cell, but it has no InCell component! Aborting." );
            return;
        }

        var coords = Get< Cell >( onCell ).coords;    
        Get< Position >( entity ).value = GetCellCenter( coords, entity );
    }

    Dictionary< uint, CellHighlight > GetAllHighlights ()
    {
        var results = new Dictionary< uint, CellHighlight >();
        var found = new List< uint >();
        GetEntitiesNonAlloc( new FilterBuilder().Include< CellHighlight >(), found );

        foreach ( var entity in found )
            results[ entity ] = Get< CellHighlight >( entity );

        return results;
    }
}
