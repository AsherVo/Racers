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

    Filter positioned = new FilterBuilder()
        .Include< GridPos >()
        .Include< Position >()
        .Include< Rect >()
        .ToFilter();

    public void StartSystem ( World world )
    {
        this.world = world;

        highlights.Clear();
        foreach ( var ( highlight, cellHighlight ) in GetAllHighlights() )
            highlights[ cellHighlight.relation ] = highlight;

        Start( positioned );
    }

    public void StopSystem ( World world )
    {
        Stop( positioned );

        highlights.Clear();
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        foreach ( var click in Read< Click >() )
        {
            if ( Exists( click.target ) && TryGet( click.target, out Cell cell ) )
                Send( new SelectCell { x = cell.coords.x, y = cell.coords.y } );
        }

        foreach ( var entity in positioned.entities )
        {
            var gridPos = Get< GridPos >( entity );
            var rect = Get< Rect >( entity );

            var position = CenterOnCell( gridPos.cell, ( int )rect.width, ( int )rect.height );
            position.x += gridIncrementX * gridPos.progress / ( float )GridPos.PROGRESS_PER_CELL;
            Get< Position >( entity ).value = position;
        }
    }

    public void DressGrid ( uint canvas )
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

    public Vector2 CenterOnCell ( Vector2Int coords, int width, int height )
    {
        var cellCenter = GetCellPosition( coords );
        cellCenter.x += cellWidth / 2;
        cellCenter.y += cellHeight / 2;

        var result = cellCenter;
        result.x -= width / 2;
        result.y -= height / 2;
        return result;
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
