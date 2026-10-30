namespace Racers.Tests;

public class RiveListDiffTests
{
    static List< uint > Run ( uint[] current, uint[] desired, out List< RiveListOp > ops )
    {
        ops = RiveListDiff.Compute( current, desired );
        var rows = new List< uint >( current );
        foreach ( var op in ops )
            RiveListDiff.Apply( rows, op );
        return rows;
    }

    [Theory]
    [InlineData( new uint[] {}, new uint[] {} )]
    [InlineData( new uint[] {}, new uint[] { 1, 2, 3 } )]
    [InlineData( new uint[] { 1, 2, 3 }, new uint[] {} )]
    [InlineData( new uint[] { 1, 2, 3 }, new uint[] { 1, 2, 3 } )]
    [InlineData( new uint[] { 1, 2, 3 }, new uint[] { 3, 2, 1 } )]
    [InlineData( new uint[] { 1, 2, 3 }, new uint[] { 2, 3, 1 } )]
    [InlineData( new uint[] { 1, 2, 3 }, new uint[] { 1, 4, 3 } )]
    [InlineData( new uint[] { 1, 2, 3 }, new uint[] { 5, 3, 6, 1 } )]
    [InlineData( new uint[] { 4, 1, 9, 2 }, new uint[] { 1, 2, 3, 4, 5 } )]
    public void ApplyingTheOpsProducesTheDesiredRows ( uint[] current, uint[] desired )
    {
        Assert.Equal( desired, Run( current, desired, out _ ) );
    }

    [Fact]
    public void UnchangedRowsProduceNoOps ()
    {
        Run( new uint[] { 1, 2, 3 }, new uint[] { 1, 2, 3 }, out var ops );
        Assert.Empty( ops );
    }

    [Fact]
    public void KeptRowsAreNeverRemovedOrReinserted ()
    {
        Run( new uint[] { 1, 2, 3, 4 }, new uint[] { 4, 7, 2, 1 }, out var ops );

        Assert.DoesNotContain( ops, op => op.type == RiveListOpType.Remove && op.row != 3 );
        Assert.DoesNotContain( ops, op => op.type == RiveListOpType.Insert && op.row != 7 );
    }

    [Fact]
    public void RemovalsComeFirstAndRunFromTheEnd ()
    {
        Run( new uint[] { 1, 2, 3, 4 }, new uint[] { 2, 4, 5 }, out var ops );

        Assert.Equal( RiveListOpType.Remove, ops[ 0 ].type );
        Assert.Equal( 2, ops[ 0 ].index );
        Assert.Equal( RiveListOpType.Remove, ops[ 1 ].type );
        Assert.Equal( 0, ops[ 1 ].index );
        Assert.Equal( RiveListOpType.Insert, ops[ 2 ].type );
    }

    [Fact]
    public void ARandomizedRunAlwaysConverges ()
    {
        var random = new Random( 1234 );
        for ( int run = 0; run < 500; run++ )
        {
            var current = Enumerable.Range( 1, 12 ).Select( i => ( uint )i ).OrderBy( _ => random.Next() ).Take( random.Next( 0, 10 ) ).ToArray();
            var desired = Enumerable.Range( 1, 12 ).Select( i => ( uint )i ).OrderBy( _ => random.Next() ).Take( random.Next( 0, 10 ) ).ToArray();

            Assert.Equal( desired, Run( current, desired, out _ ) );
        }
    }
}
