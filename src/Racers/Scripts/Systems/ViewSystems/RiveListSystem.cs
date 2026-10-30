using Engine;
using MintECS;

namespace Racers;

public class RiveListSystem : WorldManipulator, IViewSystem
{
    const string RIVE_CLICKED_TRIGGER = "clicked";

    class ScreenRows
    {
        public RiveInstance rive;
        public Dictionary< string, List< uint > > lists = new();
    }

    Filter items = new FilterBuilder()
        .Include< RiveListItem >()
        .ToFilter();

    RenderSystem renderSystem;
    Dictionary< uint, ScreenRows > screens = new();
    Dictionary< uint, uint > clickCounts = new();
    HashSet< uint > failedItems = new();

    public void StartSystem ( World world )
    {
        this.world = world;

        screens.Clear();
        clickCounts.Clear();
        failedItems.Clear();
        items.onRemove += OnRemoveItem;
        Start( items );
    }

    public void StopSystem ( World world )
    {
        Stop( items );
        items.onRemove -= OnRemoveItem;

        screens.Clear();
        clickCounts.Clear();
        failedItems.Clear();
        renderSystem = null;
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        renderSystem = Get< RenderSystem >();

        var desired = GetDesiredRows();
        foreach ( var screen in screens.Keys.ToList() )
        {
            if ( !desired.ContainsKey( screen ) )
                desired[ screen ] = screens[ screen ].lists.Keys.ToDictionary( list => list, _ => new List< uint >() );
        }

        foreach ( var ( screen, lists ) in desired )
            SyncScreen( screen, lists );

        PollClicks();
    }

    public bool TryGetRow ( uint entity, out RiveInstance rive, out string path )
    {
        rive = null;
        path = null;

        if ( !TryGet( entity, out RiveListItem item ) || !screens.TryGetValue( item.relation, out var rows ) )
            return false;

        if ( !rows.lists.TryGetValue( item.list, out var list ) )
            return false;

        int index = list.IndexOf( entity );
        if ( index < 0 )
            return false;

        rive = rows.rive;
        path = $"{ item.list }/{ index }";
        return true;
    }

    Dictionary< uint, Dictionary< string, List< uint > > > GetDesiredRows ()
    {
        var result = new Dictionary< uint, Dictionary< string, List< uint > > >();
        foreach ( var entity in items.entities )
        {
            if ( failedItems.Contains( entity ) )
                continue;

            var item = Get< RiveListItem >( entity );
            if ( item.relation == 0 || string.IsNullOrEmpty( item.list ) )
                continue;

            if ( !result.TryGetValue( item.relation, out var lists ) )
                result[ item.relation ] = lists = new();

            if ( !lists.TryGetValue( item.list, out var rows ) )
                lists[ item.list ] = rows = new();

            rows.Add( entity );
        }

        foreach ( var lists in result.Values )
        {
            foreach ( var rows in lists.Values )
                rows.Sort( ( a, b ) => GetSortOrder( a ) != GetSortOrder( b ) ? GetSortOrder( a ).CompareTo( GetSortOrder( b ) ) : a.CompareTo( b ) );
        }

        return result;
    }

    void SyncScreen ( uint screen, Dictionary< string, List< uint > > desiredLists )
    {
        var rive = Exists( screen ) ? renderSystem.GetRive( screen ) : null;
        if ( rive == null )
        {
            screens.Remove( screen );
            return;
        }

        if ( !screens.TryGetValue( screen, out var rows ) || rows.rive != rive )
            screens[ screen ] = rows = new ScreenRows { rive = rive };

        foreach ( var ( list, desired ) in desiredLists )
        {
            if ( !rows.lists.TryGetValue( list, out var current ) )
            {
                if ( !rive.ClearViewModelList( list ) )
                {
                    Log.Warning( $"{ GetName( screen ) } has no view model list '{ list }'." );
                    foreach ( var entity in desired )
                        failedItems.Add( entity );
                    continue;
                }

                rows.lists[ list ] = current = new();
            }

            foreach ( var op in RiveListDiff.Compute( current, desired ) )
                ApplyOp( rive, list, current, op );
        }
    }

    void ApplyOp ( RiveInstance rive, string list, List< uint > current, RiveListOp op )
    {
        switch ( op.type )
        {
            case RiveListOpType.Remove:
                rive.RemoveViewModelListRow( list, op.index );
                clickCounts.Remove( op.row );
                break;

            case RiveListOpType.Swap:
                rive.SwapViewModelListRows( list, op.index, op.other );
                break;

            case RiveListOpType.Insert:
                var viewModel = Get< RiveListItem >( op.row ).viewModel;
                if ( !rive.InsertViewModelListRow( list, op.index, viewModel ) )
                {
                    Log.Warning( $"Couldn't add { GetName( op.row ) } to list '{ list }': no view model '{ viewModel }'." );
                    failedItems.Add( op.row );
                    return;
                }

                if ( rive.TryGetViewModelTriggerCount( $"{ list }/{ op.index }/{ RIVE_CLICKED_TRIGGER }", out uint count ) )
                    clickCounts[ op.row ] = count;
                break;
        }

        RiveListDiff.Apply( current, op );
    }

    void PollClicks ()
    {
        foreach ( var rows in screens.Values )
        {
            foreach ( var ( list, current ) in rows.lists )
            {
                for ( int i = 0; i < current.Count; i++ )
                {
                    var entity = current[ i ];
                    if ( !rows.rive.TryGetViewModelTriggerCount( $"{ list }/{ i }/{ RIVE_CLICKED_TRIGGER }", out uint count ) )
                        continue;

                    uint fires = clickCounts.TryGetValue( entity, out uint last ) ? unchecked( count - last ) : 0;
                    clickCounts[ entity ] = count;

                    for ( uint fire = 0; fire < fires; fire++ )
                        Send( new Click { target = entity } );
                }
            }
        }
    }

    void OnRemoveItem ( uint entity )
    {
        clickCounts.Remove( entity );
        failedItems.Remove( entity );
    }

    int GetSortOrder ( uint entity )
        => TryGet( entity, out SortOrder sortOrder ) ? sortOrder : 0;
}
