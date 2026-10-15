using Engine;
using System;
using System.Linq;

namespace MintECS;

static class EmptyMessages< T > where T : Message
{
	public static readonly List< T > list = new();
}

public partial class World
{
	private static int idAt;

	public int id { get; private set; }

    public Dictionary< uint, bool > entities = new();
    public Dictionary< Type, Dictionary< uint, Component > > components = new();

    [YamlIgnore] public Dictionary< Type, List< Message > > messages = new();
    [YamlIgnore] public Dictionary< Type, List< Message > > oldMessages = new();

    // Serialization requires fields to be public
    public uint nextEntityId = 1;

    [YamlIgnore] public int structureVersion { get; private set; }

    const int MinDeadEntitiesToCompact = 1024;
    [YamlIgnore] int deadEntityCount = -1;

    [YamlIgnore] readonly Dictionary< Type, List< ISystem > > systems = new();
    [YamlIgnore] readonly List< ISystem > systemOrder = new();
    [YamlIgnore] readonly List< IDrawSystem > drawSystemOrder = new();
    [YamlIgnore] ISystem[]? systemOrderSnapshot;

    [YamlIgnore] readonly Dictionary< Type, List< Filter > > filtersByType = new();

    [YamlIgnore] readonly HashSet< uint > getSingleEntityCache = new ();
    
	public World ()
	{
		id = idAt;
		idAt++;
        
		Log.Info( $"World {id} created." );
		Inspector.Register( this );
	}

	
	#region Entities
	public uint Create ()
	{
		uint entity = 0;

		do
		{
			entity = nextEntityId;
			nextEntityId++;
		} while ( entity == 0 || Exists( entity ) );

		entities[ entity ] = true;
		return entity;
	}

	public uint Create ( string name, params Component[] components )
	{
		var entity = Create();

		Add( entity, new Name { name = name } );

		foreach ( var component in components )
			Add( entity, component );

		return entity;
	}

	public uint Create ( params Component[] components )
	{
		var entity = Create();

		foreach ( var component in components )
			Add( entity, component );

		return entity;
	}

	public void Destroy ( uint entity ) 
	{
		if ( entity == 0 )
			Log.Warning( "Got destroy request for null entity. Aborting." );
        
		if ( !Exists( entity ) )
			return;

        if ( Has< Undestroyable >( entity ) )
        {
            Log.Error( $"Attempted to destroy entity { Get< Name >( entity ).name } but it has an undestroyable component! Ignoring destroy!" );
            return;
        }
        
		// Debug.Log( $"DESTROY { GetName( entity ) }" );
        
		foreach ( var kvp in components )
        {
            var componentType = kvp.Key;
            var componentDict = kvp.Value;

			if ( componentDict.ContainsKey( entity ) )
				Remove( entity, componentType );
		}

		entities[ entity ] = false;
        structureVersion++;

        CompactEntitiesIfNeeded();
	}

    void CompactEntitiesIfNeeded ()
    {
        if ( deadEntityCount < 0 )
        {
            deadEntityCount = 0;
            foreach ( var kvp in entities )
            {
                if ( !kvp.Value )
                    deadEntityCount++;
            }
        }
        else
        {
            deadEntityCount++;
        }

        if ( deadEntityCount < MinDeadEntitiesToCompact || deadEntityCount * 2 < entities.Count )
            return;

        var compacted = new Dictionary< uint, bool >( entities.Count - deadEntityCount );
        foreach ( var kvp in entities )
        {
            if ( kvp.Value )
                compacted.Add( kvp.Key, true );
        }

        entities = compacted;
        deadEntityCount = 0;
    }

	public bool Exists ( uint entity )
		=> entities.TryGetValue( entity, out var exists ) && exists;

    public string GetName ( uint entity )
    {
    	if ( entity == 0 )
    		return "NULL";

    	if ( !Exists( entity ) )
    		return $"Non-Existant Entity-{entity}";

    	var name = Get< Name >( entity );
    	if ( name == null )
    		return $"Unnamed Entity-{entity}";
    	else
    		return $"{ name.name }-{entity}";
    }

    public string GetBasicName ( uint entity )
    {
    	if ( entity == 0 )
    		return "NULL";

    	if ( !Exists( entity ) )
    		return $"Non-Existant Entity";

    	var name = Get< Name >( entity );
    	if ( name == null )
    		return "Entity";
    	else
    		return name.name;
    }
	#endregion


	#region Components
    public T Add< T >( uint entity ) where T : Component, new()
    {
    	if ( Has< T >( entity ) )
    		return Get< T >( entity );

    	return Add( entity, new T() );	    	
    }

    public Component Add ( uint entity, System.Type type )
    {
    	if ( Has( entity, type ) )
    		return Get( entity, type );

    	var component = ( Component )System.Activator.CreateInstance( type )!;
    	return Add( entity, component );
    }

    public T Add< T >( uint entity, T component ) where T : Component 
    {
    	var type = component.GetType();
    	if ( !components.ContainsKey( type ) )
    		components[ type ] = new();

    	if ( components[ type ].ContainsKey( entity ) )
    		Remove( entity, type );

    	components[ type ][ entity ] = component;
        structureVersion++;

        if ( !filtersByType.TryGetValue( type, out var watchingFilters ) )
            return component;

        bool isRelation = component is Relation;
    	foreach ( var filter in watchingFilters )
		{
			if ( filter.Includes( type ) )
				UpdateFilterWithAdd( filter, entity );
			else if ( filter.Excludes( type ) )
				UpdateFilterWithRemove( filter, entity );

			if ( isRelation )
			{
				if ( filter.Relates( type ) )
					UpdateFilterWithAdd( filter, entity );
				else if ( filter.NotRelates( type ) )
					UpdateFilterWithRemove( filter, entity );
			}
		}
    		
    	return component;	    	
    }

    public void AddBatch ( uint entity, params Component[] components )
    {
    	foreach ( var component in components )
    		Add( entity, component );  	
    }

    public void Remove< T >( uint entity ) where T : Component
    	=> Remove( entity, typeof( T ) );

    public void Remove( uint entity, System.Type type )
    {
    	if ( !components.ContainsKey( type ) )
    		return;

    	if ( !components[ type ].ContainsKey( entity ) )
    		return;


    	var component = components[ type ][ entity ];
    	components[ type ].Remove( entity );
        structureVersion++;

        if ( !filtersByType.TryGetValue( type, out var watchingFilters ) )
            return;

        bool isRelation = component is Relation;
		foreach ( var filter in watchingFilters )
		{
			if ( filter.Includes( type ) )
				UpdateFilterWithRemove( filter, entity );
			else if ( filter.Excludes( type ) )
				UpdateFilterWithAdd( filter, entity );

			if ( isRelation )
			{
				if ( filter.Relates( type ) )
					UpdateFilterWithRemove( filter, entity );
				else if ( filter.NotRelates( type ) )
					UpdateFilterWithAdd( filter, entity );
			}
		}
    }

    public bool Has< T >( uint entity )  where T : Component 
   		=> Has( entity, typeof( T ) );
   
   	public bool Has ( uint entity, System.Type type )
   	{
   		if ( entity == 0 )
   			return false;

   		if ( !Exists( entity ) )
   		{
   			Log.Warning( $"Trying to find component ({ type }) from entity that does not exist ({ entity }) (World {id})." );
   			return false;
   		}

    	return components.TryGetValue( type, out var componentDict ) && componentDict.ContainsKey( entity );
   	}

    public T Get< T >( uint entity )  where T : Component 
   		=> ( T )Get( entity, typeof( T ) );

    public Component Get ( uint entity, System.Type type )
    {
    	if ( !Exists( entity ) )
   		{
   			Log.Warning( $"Trying to get component ({ type }) from entity that does not exist ({ entity }) (World {id})." );
   			return null;
   		}

    	if ( components.TryGetValue( type, out var componentDict ) && componentDict.TryGetValue( entity, out var component ) )
    		return component;

    	return null;
    }

    public bool TryGet< T > ( uint entity, out T component ) where T : Component
    {
        component = null;

        if ( !Exists( entity ) )
            return false;

        if ( !components.TryGetValue( typeof ( T ), out var componentDict ) || !componentDict.TryGetValue( entity, out var found ) )
            return false;

        component = ( T ) found;
        return true;
    }

    public T GetOrAdd< T > ( uint entity ) where T : Component, new()
    {
    	if ( !Has< T >( entity ) )
    		return Add< T >( entity );
    	else
    		return Get< T >( entity );
    }

    public Component GetOrAdd ( uint entity, System.Type type )
    {
    	if ( !Has( entity, type ) )
    		return Add( entity, type );
    	else
    		return Get( entity, type );
    }

    public List< Component > GetComponents ( uint entity )
    {
    	var results = new List< Component >();
    	foreach ( var dict in components.Values )
    	{
    		if ( dict.ContainsKey( entity ) )
    			results.Add( dict[ entity ] );
    	}

    	return results;
    }
    #endregion


    #region Systems
	public void Start ( ISystem system )
    {
    	var type = system.GetType();
    	if ( !systems.ContainsKey( type ) )
    		systems[ type ] = new();

    	systems[ type ].Add( system );
    	systemOrder.Add( system );
        systemOrderSnapshot = null;

        if ( system is IDrawSystem )
        	drawSystemOrder.Add( ( system as IDrawSystem )! );

    	system.StartSystem( this );
    }

    public void Stop ( ISystem system )
    {
    	var type = system.GetType();
    	systemOrder.Remove( system );

    	if ( system is IDrawSystem )
    		drawSystemOrder.Remove( ( system as IDrawSystem )! );

        systemOrderSnapshot = null;
    	systems[ type ].Remove( system );

    	if ( systems[ type ].Count == 0 )
    		systems.Remove( type );

    	system.StopSystem( this );
    }

    public T Get< T > () where T : ISystem
    	=> ( T )systems[ typeof( T ) ][ 0 ];

    public bool TryGet< T > ( out T result ) where T : ISystem
    {
        if ( systems.TryGetValue( typeof( T ), out var sysList ) )
        {
            result = ( T ) sysList[ 0 ];
            return true;
        } 

        result = default;
        return false;
    }
    
    #endregion

    #region Relations
    public T SetRelation< T >( uint fromEntity, uint toEntity ) where T : Relation, new()
    	=> SetRelation( fromEntity, toEntity, new T() );

    public T SetRelation< T >( uint fromEntity, uint toEntity, T relation ) where T : Relation, new()
    {
    	if ( HasRelation< T >( fromEntity, toEntity ) )
    	{
    		return GetRelation< T >( fromEntity, toEntity );
    	}
    	
    	T component;
    	if ( Has< T >( fromEntity ) )
    	{
    		component = Get< T >( fromEntity );
    		component.relation = toEntity;
            structureVersion++;
    	}
    	else
    	{
    		component = Add( fromEntity, new T { relation = toEntity } );
    	}

        if ( !filtersByType.TryGetValue( typeof( T ), out var watchingFilters ) )
            return component;

		foreach ( var filter in watchingFilters )
		{
			if ( filter.Relates( typeof( T ) ) )
				UpdateFilterWithAdd( filter, fromEntity );
			else if ( filter.NotRelates( typeof( T ) ) )
				UpdateFilterWithRemove( filter, fromEntity );
		}

    	return component;    	
    }

    public void RemoveRelation< T > ( uint fromEntity, uint toEntity ) where T : Relation
    {
    	var relation = GetRelation< T >( fromEntity, toEntity );
    	if ( relation == null )
    		return;

    	Remove< T >( fromEntity );

        if ( !filtersByType.TryGetValue( typeof( T ), out var watchingFilters ) )
            return;

		foreach ( var filter in watchingFilters )
		{
			if ( filter.Relates( typeof( T ) ) )
				UpdateFilterWithRemove( filter, fromEntity );
			else if ( filter.NotRelates( typeof( T ) ) )
				UpdateFilterWithAdd( filter, fromEntity );
		}
    }

    public bool HasRelation< T > ( uint fromEntity, uint toEntity ) where T : Relation
    {
        if ( TryGet< T >( fromEntity, out var relation ) )
        {
            return relation.relation == toEntity;
        }

        return false;
    }

    public bool HasRelation ( uint fromEntity, uint toEntity, Type type )
    {
        if ( Get( fromEntity, type ) is Relation relation )
    		return relation.relation == toEntity;
    	else
    		return false;
    }

    public T GetRelation< T >( uint fromEntity, uint toEntity ) where T : Relation
    {
    	var relation = Get< T >( fromEntity );
    	if ( relation != null )
    	{
    		if ( relation.relation != toEntity )
    			relation = null;
    	}
    	return relation;
    }

    public List< uint > GetEntities< T > ( uint entity ) where T : Relation
    	=> GetRelations< T >( entity ).Keys.ToList();

    public Dictionary< uint, T > GetRelations< T >( uint entity ) where T : Relation
    {
    	var type = typeof( T );
    	var results = new Dictionary< uint, T >();
    	if ( !components.ContainsKey( type ) )
    		return results;

		var relationDict = components[ type ];
		foreach ( var kvp in relationDict )
		{
			if ( !Exists( kvp.Key ) )
				continue;

			var relation = ( Relation )kvp.Value;
			if ( relation.relation == entity )
				results[ kvp.Key ] = ( T )relation;
		}

		return results;
    }

    public uint GetRelated< T >( uint fromEntity ) where T : Relation
    {
    	if ( !Has< T >( fromEntity ) )
    		return 0;

    	var relation = Get< T >( fromEntity ) as Relation;
    	if ( !Exists( relation.relation ) )
    		return 0;

    	return relation.relation;
    }
    #endregion


    #region Messages
    public T Send< T > () where T: Message, new ()
    	=> Send< T >( new T() );

    public T Send< T >( T message ) where T : Message 
    {
    	var type = message.GetType();
    	if ( !messages.ContainsKey( type ) )
    		messages[ type ] = new List< Message >();

    	messages[ type ].Add( message );
    	return message;
    }

    public List< T > Read< T >() where T : Message
    	=> CopyMessages< T >( messages );

    public List< Message > Read ( System.Type messageType )
    {
    	if ( !messages.ContainsKey( messageType ) )
    		return new List< Message >();

    	return messages[ messageType ];
    }

    public List< T > ReadOld< T >() where T : Message
    	=> CopyMessages< T >( oldMessages );

    static List< T > CopyMessages< T > ( Dictionary< Type, List< Message > > source ) where T : Message
    {
    	if ( !source.TryGetValue( typeof( T ), out var list ) || list.Count == 0 )
    		return EmptyMessages< T >.list;

    	var results = new List< T >( list.Count );
    	for ( int i = 0; i < list.Count; i++ )
    		results.Add( ( T )list[ i ] );

    	return results;
    }

    static T FirstMessage< T > ( Dictionary< Type, List< Message > > source ) where T : Message
    	=> source.TryGetValue( typeof( T ), out var list ) && list.Count > 0 ? ( T )list[ 0 ] : null;

    public List< Message > ReadOld ( System.Type messageType )
    {
    	if ( !oldMessages.ContainsKey( messageType ) )
    		return new List< Message >();

    	return oldMessages[ messageType ];
    }

    public T ReadSingle< T > () where T : Message
    	=> FirstMessage< T >( messages );

    public T ReadSingleOld< T > () where T : Message
    	=> FirstMessage< T >( oldMessages );

    public Message ReadSingle ( System.Type type )
    {
    	var results = Read( type );
    	return results.Count == 0 ? null : results[ 0 ];
    }

    public Message ReadSingleOld ( System.Type type )
    {
    	var results = ReadOld( type );
    	return results.Count == 0 ? null : results[ 0 ];
    }

    public void Destroy ( Message message )
	{
		var type = message.GetType();
		if ( !messages.ContainsKey( type ) )
			return;

		if ( messages[ type ].Contains( message ) )
			messages[ type ].Remove( message );
	}
    
    public void DestroyOld ( Message message )
    {
        var type = message.GetType();
        if ( !messages.ContainsKey( type ) )
            return;

        if ( oldMessages[ type ].Contains( message ) )
            oldMessages[ type ].Remove( message );
    }
	#endregion


	#region Filters
    public void Start ( Filter filter )
    {    	
    	if ( filter.isBeingModified )
    		Log.Error( "Filter is being recursively modified! This is not allowed." );

    	filter.isBeingModified = true;

        foreach ( var type in filter.watchedTypes )
        {
            if ( !filtersByType.TryGetValue( type, out var watchingFilters ) )
            {
                watchingFilters = new List< Filter >();
                filtersByType[ type ] = watchingFilters;
            }

            watchingFilters.Add( filter );
        }

		filter.entities = new HashSet< uint >(
    		entities
    		.Where( kvp => kvp.Value == true )
    		.Select( kvp => kvp.Key )
	        .Where( entity => filter.Matches( entity, this ) )
	    );

	    foreach ( var entity in filter.entities )
	    	filter.onAdd?.Invoke( entity );

	    filter.isBeingModified = false;
    }

    public void Stop ( Filter filter )
    {
    	if ( filter.isBeingModified )
    		Log.Error( "Filter is being recursively modified! This is not allowed." );

    	filter.isBeingModified = true;

    	foreach ( var entity in filter.entities)
	        filter.onRemove?.Invoke( entity );

    	filter.entities.Clear();
        foreach ( var type in filter.watchedTypes )
        {
            if ( filtersByType.TryGetValue( type, out var watchingFilters ) )
                watchingFilters.Remove( filter );
        }

    	filter.isBeingModified = false;
    }

    public void GetEntitiesNonAlloc ( FilterBuilder filterBuilder, HashSet< uint > results )
    {
		foreach ( KeyValuePair< uint, bool > kvp in entities )
        {
            if ( kvp.Value == true && filterBuilder.Matches( kvp.Key, this ) )
            {
                results.Add( kvp.Key );
            }
        }
    }
    
    public void GetEntitiesNonAlloc ( FilterBuilder filterBuilder, List< uint > results )
    {
        foreach ( KeyValuePair< uint, bool > kvp in entities )
        {
            if ( kvp.Value == true && filterBuilder.Matches( kvp.Key, this ) )
            {
                results.Add( kvp.Key );
            }
        }
    }

    public uint GetSingleEntity ( FilterBuilder filterBuilder )
    {
        getSingleEntityCache.Clear();
    	GetEntitiesNonAlloc( filterBuilder, getSingleEntityCache );

        if ( getSingleEntityCache.Any() )
            return getSingleEntityCache.First();
        else
            return 0;
    }

    public void Tick ()
    {	
    	// Don't clear messages early, so we can recieve messages
    	// from ui events

    	// Tick Systems
    	systemOrderSnapshot ??= systemOrder.ToArray();
    	var systemListCopy = systemOrderSnapshot;
    	foreach ( var system in systemListCopy )
    		system.TickSystem( this );

    	// Move messages to old messages
		oldMessages.Clear();
		foreach ( var kvp in messages )
		    oldMessages.Add( kvp.Key, kvp.Value );

		// Clear messages
		messages.Clear();
    }

    public void Draw ( SpriteBatch batch )
    {
    	foreach ( var system in drawSystemOrder )
    		system.DrawSystem( this, batch );
    }

    // Extremely inefficient. Shoudln't ever be used.
    void RefreshFilter ( Filter filter )
    {
    	if ( filter.isBeingModified )
    		Log.Error( "Filter is being recursively modified! This is not allowed." );

    	filter.isBeingModified = true;

    	var newEntities = new HashSet< uint >(
    		entities
    		.Where( kvp => kvp.Value == true )
    		.Select( kvp => kvp.Key )
	        .Where( entity => filter.Matches( entity, this ) )
	    );

	    filter.entities = newEntities;

	    var toAdd = new HashSet< uint >( newEntities );
	    toAdd.ExceptWith( filter.entities );

	    var toRemove = new HashSet< uint >( filter.entities );
	    toRemove.ExceptWith( newEntities );

	    foreach ( var entity in toRemove)
	        filter.onRemove?.Invoke( entity );

	    foreach ( var entity in toAdd )
	        filter.onAdd?.Invoke( entity );

	    filter.isBeingModified = false;
    }

    void UpdateFilterWithAdd ( Filter filter, uint addedEntity )
    {
    	if ( filter.entities.Contains( addedEntity ) )
    		return;

    	if ( !filter.Matches( addedEntity, this ) )
    		return;

    	if ( filter.isBeingModified )
    		Log.Error( "Filter is being recursively modified! This is not allowed." );

    	filter.isBeingModified = true;

    	filter.entities.Add( addedEntity );
    	filter.onAdd?.Invoke( addedEntity );

    	filter.isBeingModified = false;
    }

    void UpdateFilterWithRemove ( Filter filter, uint removedEntity )
    {
    	if ( !filter.entities.Contains( removedEntity ) )
    		return;

    	if ( filter.Matches( removedEntity, this ) )
    		return;

    	if ( filter.isBeingModified )
    		Log.Error( "Filter is being recursively modified! This is not allowed." );

    	filter.isBeingModified = true;

    	filter.entities.Remove( removedEntity );
    	filter.onRemove?.Invoke( removedEntity );

    	filter.isBeingModified = false;
    }
    #endregion

    // SLOW, USE FOR DEBUG ONLY
    public List< uint > GetEntities ()
    {
    	return entities
    		.Where( kvp => kvp.Value == true )
    		.Select( kvp => kvp.Key )
    		.ToList();
    }
}