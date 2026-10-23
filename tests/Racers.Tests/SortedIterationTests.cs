using Engine;
using MintECS;

namespace Racers.Tests;

public class SortedIterationTests
{
    static Filter UnitFilter ()
        => new FilterBuilder().Include< Unit >().ToFilter();

    [Fact]
    public void FiltersIterateInIdOrderWhateverTheHistory ()
    {
        var world = new World( 60 );
        var ids = Enumerable.Range( 0, 20 ).Select( _ => world.Create() ).ToList();

        var filter = UnitFilter();
        world.Start( filter );

        foreach ( int i in new[] { 13, 2, 19, 7, 0, 11, 4 } )
            world.Add( ids[ i ], new Unit() );

        world.Destroy( ids[ 7 ] );
        world.Remove< Unit >( ids[ 0 ] );
        world.Add( ids[ 0 ], new Unit() );

        var expected = new[] { 0, 2, 4, 11, 13, 19 }.Select( i => ids[ i ] ).ToList();
        Assert.Equal( expected, filter.entities.ToList() );
        Assert.Equal( ids[ 0 ], filter.FirstOrDefault() );
        Assert.True( filter.entities.Contains( ids[ 11 ] ) );
        Assert.False( filter.entities.Contains( ids[ 7 ] ) );
    }

    [Fact]
    public void FilterStartsVisitEntitiesInIdOrder ()
    {
        var world = new World( 60 );
        var ids = Enumerable.Range( 0, 10 ).Select( _ => world.Create() ).ToList();
        foreach ( int i in new[] { 9, 3, 6, 1 } )
            world.Add( ids[ i ], new Unit() );

        var added = new List< uint >();
        var filter = UnitFilter();
        filter.onAdd = added.Add;
        world.Start( filter );

        Assert.Equal( new[] { 1, 3, 6, 9 }.Select( i => ids[ i ] ), added );
    }

    [Fact]
    public void FilterOrderIsTheSameAfterALoad ()
    {
        var world = Match.Create();
        Match.Run( world, 50 );

        var filter = new FilterBuilder().Include< Cell >().ToFilter();
        world.Start( filter );

        var loaded = World.Load( world.Save() );
        var loadedFilter = new FilterBuilder().Include< Cell >().ToFilter();
        loaded.Start( loadedFilter );

        Assert.Equal( filter.entities.ToList(), loadedFilter.entities.ToList() );
    }

    [Fact]
    public void RelationQueriesAreInIdOrder ()
    {
        var world = new World( 60 );
        var target = world.Create();
        var ids = Enumerable.Range( 0, 8 ).Select( _ => world.Create() ).ToList();
        foreach ( int i in new[] { 5, 1, 7, 3 } )
            world.SetRelation< InCell >( ids[ i ], target );

        var expected = new[] { 1, 3, 5, 7 }.Select( i => ids[ i ] ).ToList();
        Assert.Equal( expected, world.GetEntities< InCell >( target ) );
        Assert.Equal( expected, world.GetRelations< InCell >( target ).Select( pair => pair.entity ) );
    }

    [Fact]
    public void QueriesReturnLowestIdsFirst ()
    {
        var world = new World( 60 );
        var ids = Enumerable.Range( 0, 6 ).Select( _ => world.Create() ).ToList();
        foreach ( int i in new[] { 4, 2, 5 } )
            world.Add( ids[ i ], new Unit() );

        var query = new FilterBuilder().Include< Unit >();
        Assert.Equal( ids[ 2 ], world.GetSingleEntity( query ) );

        var results = new List< uint > { 999 };
        world.GetEntitiesNonAlloc( query, results );
        Assert.Equal( new uint[] { 999, ids[ 2 ], ids[ 4 ], ids[ 5 ] }, results );
    }

    [Fact]
    public void DestroyRemovesComponentsInTypeNameOrder ()
    {
        var world = new World( 60 );
        var target = world.Create();
        var entity = world.Create();

        // Unit is seen first, so hash order would remove it first; Racers.InCell sorts before Racers.Unit.
        world.Add( entity, new Unit() );
        world.SetRelation< InCell >( entity, target );

        var removed = new List< string >();
        var units = UnitFilter();
        units.onRemove = _ => removed.Add( nameof( Unit ) );
        var inCells = new FilterBuilder().Include< InCell >().ToFilter();
        inCells.onRemove = _ => removed.Add( nameof( InCell ) );
        world.Start( units );
        world.Start( inCells );

        world.Destroy( entity );

        Assert.Equal( new[] { nameof( InCell ), nameof( Unit ) }, removed );
    }
}
