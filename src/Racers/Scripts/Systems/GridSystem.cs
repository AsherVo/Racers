using Engine;
using MintECS;

namespace Racers;

public class GridSystem : WorldManipulator, ISimSystem
{
    public uint grid { get; private set; }
    public uint[] cells { get; private set; } = Array.Empty< uint >();

    public int width => grid != 0 ? Get< Grid >( grid ).width : 0;
    public int height => grid != 0 ? Get< Grid >( grid ).height : 0;
    public int size => width * height;

    public void StartSystem ( World world )
    {
        this.world = world;

        grid = GetSingleEntity( new FilterBuilder().Include< Grid >() );
        RebuildCells();
    }

    public void StopSystem ( World world )
    {
        grid = 0;
        cells = Array.Empty< uint >();
        this.world = null;
    }

    public void FixedUpdate ( World world )
    {
    }

    public uint SpawnGrid ( int width, int height )
    {
        grid = Create( "Grid", new Grid { width = width, height = height } );

        for ( int y = 0; y < height; y++ )
        {
            for ( int x = 0; x < width; x++ )
                Create( $"Cell ({ x }, { y })", new Cell { relation = grid, coords = new Vector2Int( x, y ) } );
        }

        RebuildCells();
        return grid;
    }

    void RebuildCells ()
    {
        cells = new uint[ size ];
        if ( grid == 0 )
            return;

        foreach ( var ( entity, cell ) in GetRelations< Cell >( grid ) )
        {
            if ( InBounds( cell.coords ) )
                cells[ OneDee( cell.coords ) ] = entity;
        }
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

    public int OneDee ( Vector2Int coords )
    {
        return ( coords.y * width ) + coords.x;
    }

    public Vector2Int TwoDee ( int index )
    {
        int x = index % width;
        int y = index / width;
        return new Vector2Int( x, y );
    }
}
