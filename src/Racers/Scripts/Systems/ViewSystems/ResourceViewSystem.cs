using Engine;
using MintECS;

namespace Racers;

public class ResourceViewSystem : WorldManipulator, IViewSystem
{
    Filter filter;
    Dictionary< uint, ( RiveInstance rive, string resourceId, int value ) > shownValues = new();

    public void StartSystem ( World world )
    {
        this.world = world;

        filter = new FilterBuilder()
            .Include< ResourceView >()
            .ToFilter();

        shownValues.Clear();

        filter.onRemove += OnRemoveResourceView;
        Start( filter );
    }

    public void StopSystem ( World world )
    {
        Stop( filter );
        filter.onRemove -= OnRemoveResourceView;

        shownValues.Clear();

        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        var renderSystem = Get< RenderSystem >();
        var resourceSystem = Get< ResourceSystem >();

        foreach ( var entity in filter.entities )
        {
            var resourceView = Get< ResourceView >( entity );
            
            var view = resourceView.relation;
            if ( view == 0 )
                continue;

            if ( string.IsNullOrEmpty( resourceView.resourceId ) )
                continue;

            var rive = renderSystem.GetRive( view );
            if ( rive == null )
            {
                Log.Warning( $"{ GetName( entity ) } is a resource view pointing at view '{ GetName( view ) }', but { GetName( view ) } has no Rive! Aborting." );
                continue;
            }

            int displayValue = 0;

            if ( resourceView.unit != 0 )
                displayValue = resourceSystem.GetValue( resourceView.unit, resourceView.resourceId );
            
            if ( shownValues.TryGetValue( entity, out var shown ) && shown.rive == rive && shown.resourceId == resourceView.resourceId && shown.value == displayValue )
                continue;

            rive.SetViewModelString( resourceView.resourceId, displayValue.ToString() );
            shownValues[ entity ] = ( rive, resourceView.resourceId, displayValue );
        }
    }

    void OnRemoveResourceView ( uint entity )
        => shownValues.Remove( entity );

    public uint Connect ( uint view, uint unit, string resourceId )
    {
        return Create( 
            $"{ GetBasicName( view ) } - { resourceId }", 
            new ResourceView { 
                relation = view, 
                unit = unit, 
                resourceId = resourceId 
            }
        );
    }
}