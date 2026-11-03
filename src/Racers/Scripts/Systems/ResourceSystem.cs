using Engine;
using MintECS;

namespace Racers;

public class ResourceSystem : WorldManipulator, ISimSystem
{
    Filter filter;
    Dictionary< uint, Dictionary< string, uint > > lookup;
    Dictionary< uint, ( uint parent, string id ) > lookupKeys;

    public void StartSystem ( World world )
    {
        this.world = world;

        filter = new FilterBuilder()
            .Include< Resource >()
            .Include< DataId >()
            .ToFilter();

        lookup = new();
        lookupKeys = new();
        filter.onAdd += OnAddResource;
        filter.onRemove += OnRemoveResource;

        Start( filter );
    }

    public void StopSystem ( World world )
    {
        Stop( filter );

        lookup.Clear();
        lookupKeys.Clear();
        filter.onAdd -= OnAddResource;
        filter.onRemove -= OnRemoveResource;
    }

    public void FixedUpdate ( World world )
    {

    }

    public uint Get ( uint entity, string id )
    {
        if ( Toolbox.I.data.GetGroup( id ) != Group.Resources )
        {
            Log.Warning( $"ResourceSystem: Cannot get { id }, it's not a Resource! Aborting." );
            return 0;
        }

        if ( !lookup.TryGetValue( entity, out var resources ) )
            return 0;

        return resources.TryGetValue( id, out var resource ) ? resource : 0;
    }

    public int GetValue ( uint entity, string id )
    {
        var resourceEntity = Get( entity, id );
        return resourceEntity == 0 ? 0 : Get< Resource >( resourceEntity ).value;
    }

    public uint Add ( uint entity, string id, int startValue = 0 )
    {
        if ( Toolbox.I.data.GetGroup( id ) != Group.Resources )
        {
            Log.Warning( $"ResourceSystem: Cannot add { id }, it's not a Resource! Aborting." );
            return 0;
        }

        var prefab = Get< PrefabSystem >();
        var resource = prefab.CreateFromData( id, $"{ GetName( entity ) } - { id }" );

        Add( resource, new Resource {
            relation = entity,
            value = startValue
        });

        return resource;
    }

    public void Set ( uint entity, string id, int value )
    {
        var resourceEntity = Get( entity, id );
        if ( resourceEntity == 0 )
        {
            Add( entity, id, value );
            return;
        }

        Get< Resource >( resourceEntity ).value = value;
        // todo: messages
    }

    void OnAddResource ( uint entity )
    {
        string id = Get< DataId >( entity );
        var resource = Get< Resource >( entity );

        var parent = resource.relation;
        if ( parent == 0 )
        {
            Log.Warning( $"ResourceSystem: { GetName( entity ) } is a resource without a relation! Aborting." );
            return;
        }

        if ( !lookup.ContainsKey( parent ) )
            lookup[ parent ] = new();

        lookup[ parent ][ id ] = entity;
        lookupKeys[ entity ] = ( parent, id );
    }

    void OnRemoveResource ( uint entity )
    {
        if ( !lookupKeys.Remove( entity, out var key ) )
            return;

        var resources = lookup[ key.parent ];
        if ( resources.TryGetValue( key.id, out var current ) && current == entity )
            resources.Remove( key.id );

        if ( resources.Count == 0 )
            lookup.Remove( key.parent );
    }

}
