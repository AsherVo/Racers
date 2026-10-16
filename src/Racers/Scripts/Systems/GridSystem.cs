using RectangleF = System.Drawing.RectangleF;
using System.Runtime.InteropServices;
using Engine;
using MintECS;
using Vo;

namespace Racers;

public class GridSystem : WorldManipulator, ISystem
{
    public int width { get; private set; } = 9;
    public int height { get; private set; } = 3;
    
    public uint[] cells { get; private set; }
    public uint[] highlights { get; private set; }

    public int size => width * height;

    const int gridStartX = 10;
    const int gridStartY = 2;
    const int yOffset = 6;
    const int gridIncrementX = 40;
    const int gridIncrementY = 34;

    const int cellWidth = 40;
    const int cellHeight = 30;

    public void StartSystem ( World world )
    {
        this.world = world;
    }

    public void StopSystem ( World world )
    {
        this.world = null;
    }

    public void TickSystem ( World world )
    {   
        
    }

    public void SpawnGrid ( uint canvas )
    {
        cells = new uint[ size ];
        highlights = new uint[ size ];

        var prefab = Get< PrefabSystem >();

        for ( int y = 0; y < height; y++ )
        {
            for ( int x = 0; x < width; x++ )
            {
                var coords = new Vector2Int( x, y );
                var cell = prefab.CreateFromData( "Cell", canvas, $"Cell ({ x }, { y })" );
                Get< Position >( cell ).value = GetCellPosition( coords ) ;

                Add( cell, new Cell { coords = coords  } );
                Add( cell, new SortOrder { value = -1 } );
                cells[ OneDee( coords ) ] = cell;

                // var highlight = world.Create( $"Cell ({ x }, { y }) Highlight", 
                //     new Position {},
                //     new Child {
                //         relationId = cell,
                //     },
                //     new TransformChild {
                //         relationId = cell,
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
                // highlights[ OneDee( coords ) ] = highlight;
            }
        }
    }

    uint GetHighlight ( uint cellEntity )
    {
        var cell = Get< Cell >( cellEntity );
        return highlights[ OneDee( cell.coords ) ];
    }

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

    public bool InBounds ( Vector2Int coords )
        => coords.x >= 0 && coords.x < width && coords.y >= 0 && coords.y < height;

    public uint GetCell ( Vector2Int coords )
        => cells[ OneDee( coords ) ];

    public List< uint > GetContents ( Vector2Int coords )
        => GetEntities< InCell >( GetCell( coords ) );

    public uint Get< T > ( Vector2Int coords ) where T : Component
        => GetEntities< InCell >( GetCell( coords ) )
            .SingleOrDefault( e => Has< T >( e ) );

    public List< uint > GetAll< T > ( Vector2Int coords ) where T : Component
        => GetEntities< InCell >( GetCell( coords ) )
            .Where( e => Has< T >( e ) )
            .ToList();

    public List< uint > GetAll ()
        => cells.ToList();

    int OneDee ( Vector2Int coords )
    {
        return ( coords.y * width ) + coords.x;
    }

    Vector2Int TwoDee ( int index )
    {
        int x = index % width;
        int y = index / width;
        return new Vector2Int( x, y );
    }
}