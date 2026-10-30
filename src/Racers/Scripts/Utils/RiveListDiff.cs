using System.Collections.Generic;

namespace Racers;

public enum RiveListOpType
{
    Remove,
    Swap,
    Insert
}

public readonly record struct RiveListOp ( RiveListOpType type, int index, int other, uint row );

public static class RiveListDiff
{
    public static List< RiveListOp > Compute ( IReadOnlyList< uint > current, IReadOnlyList< uint > desired )
    {
        var ops = new List< RiveListOp >();
        var rows = new List< uint >( current );
        var wanted = new HashSet< uint >( desired );

        for ( int i = rows.Count - 1; i >= 0; i-- )
        {
            if ( wanted.Contains( rows[ i ] ) )
                continue;

            ops.Add( new RiveListOp( RiveListOpType.Remove, i, 0, rows[ i ] ) );
            rows.RemoveAt( i );
        }

        for ( int i = 0; i < desired.Count; i++ )
        {
            if ( i < rows.Count && rows[ i ] == desired[ i ] )
                continue;

            int found = rows.IndexOf( desired[ i ], i );
            if ( found >= 0 )
            {
                ops.Add( new RiveListOp( RiveListOpType.Swap, i, found, desired[ i ] ) );
                ( rows[ i ], rows[ found ] ) = ( rows[ found ], rows[ i ] );
            }
            else
            {
                ops.Add( new RiveListOp( RiveListOpType.Insert, i, 0, desired[ i ] ) );
                rows.Insert( i, desired[ i ] );
            }
        }

        return ops;
    }

    public static void Apply ( List< uint > rows, RiveListOp op )
    {
        switch ( op.type )
        {
            case RiveListOpType.Remove:
                rows.RemoveAt( op.index );
                break;

            case RiveListOpType.Swap:
                ( rows[ op.index ], rows[ op.other ] ) = ( rows[ op.other ], rows[ op.index ] );
                break;

            case RiveListOpType.Insert:
                rows.Insert( op.index, op.row );
                break;
        }
    }
}
