using Engine;
using MintECS;

namespace Racers;

public class TransformSystem : WorldManipulator, IViewSystem
{
    Filter filter;

    public void StartSystem ( World world )
    {
        this.world = world;

        filter = new FilterBuilder()
            .Include< Position >()
            .Include< TransformChild >()
            .ToFilter();

        filter.onAdd += OnAddChild;
        
        Start( filter );
    }

    public void StopSystem ( World world )
    {
        filter.onAdd -= OnAddChild;
        Stop( filter );
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        var orphans = new List< uint >();
        foreach ( var child in filter.GetListCopy() )
        {
            var parent = GetRelated< TransformChild >( child );
            if ( parent == 0 )
            {
                orphans.Add( child );
                continue;
            }

            var childComponent = Get< TransformChild >( child );
            var parentPosition = Get< Position >( parent ).value;
            Get< Position >( child ).value = parentPosition + childComponent.offset;
        }

        foreach ( var child in orphans )
            Remove< TransformChild >( child );
    }

    void OnAddChild ( uint child )
    {
        var parent = GetRelated< TransformChild >( child );
        var parentPosition = Get< Position >( parent ).value;
        Get< Position >( child ).value = parentPosition;
    }

    public void SetParent ( uint entity, uint parent )
    {
        var offset = Get< Position >( entity ).value - Get< Position >( parent ).value;
        Add( entity, new TransformChild { relation = parent, offset = offset } );
    }

    public List< uint > GetChildren ( uint entity )
        => GetEntities< TransformChild >( entity );
}