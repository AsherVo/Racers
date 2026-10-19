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
	static bool SimComponentsValidated;

	public int id { get; private set; }

    /// <summary>Entities created in the view phase get ids from here up, so they never shift a sim entity's id.</summary>
    public const uint VIEW_ENTITY_ID_START = 1_000_000;

    public Dictionary< uint, bool > entities = new();
    public Dictionary< Type, Dictionary< uint, Component > > components = new();

    /// <summary>View messages, plus every sim message sent by the ticks since the last <see cref="Update"/>.</summary>
    [YamlIgnore] public Dictionary< Type, List< Message > > messages = new();
    [YamlIgnore] Dictionary< Type, List< Message > > simMessages = new();

    // Serialization requires fields to be public
    public uint nextEntityId = 1;
    public uint nextViewEntityId = VIEW_ENTITY_ID_START;

    /// <summary>How many fixed ticks have been simulated. The only clock the sim sees.</summary>
    public long tick;
    public int ticksPerSecond;

    /// <summary>Commands sent since the last tick. The next <see cref="FixedTick"/> stamps and applies them.</summary>
    public List< Command > pendingCommands = new();

    [YamlIgnore] public WorldPhase phase { get; private set; }

    [YamlIgnore] public int structureVersion { get; private set; }

    const int MinDeadEntitiesToCompact = 1024;
    [YamlIgnore] int deadEntityCount = -1;

    [YamlIgnore] readonly Dictionary< Type, List< ISystem > > systems = new();
    [YamlIgnore] readonly List< ISimSystem > simSystemOrder = new();
    [YamlIgnore] readonly List< IViewSystem > viewSystemOrder = new();
    [YamlIgnore] readonly List< IDrawSystem > drawSystemOrder = new();
    [YamlIgnore] ISimSystem[]? simSystemSnapshot;
    [YamlIgnore] IViewSystem[]? viewSystemSnapshot;

    [YamlIgnore] readonly Dictionary< Type, List< Filter > > filtersByType = new();

    [YamlIgnore] readonly HashSet< uint > getSingleEntityCache = new ();

	public World ( int ticksPerSecond ) : this()
	{
		if ( ticksPerSecond <= 0 )
			throw new ArgumentOutOfRangeException( nameof( ticksPerSecond ), ticksPerSecond, "Must be positive." );

		this.ticksPerSecond = ticksPerSecond;
	}

	World ()
	{
		id = idAt;
		idAt++;

		if ( !SimComponentsValidated )
		{
			SimComponentRules.ValidateAssembly( typeof( World ).Assembly );
			SimComponentsValidated = true;
		}

		Log.Info( $"World {id} created." );
		Inspector.Register( this );
	}

	
	#region Entities
	/// <summary>
	/// In the view phase, takes the next id from the view range. Everywhere else (loading, ticks) takes the next sim
	/// id, so sim ids only depend on what the sim does.
	/// </summary>
	public uint Create ()
	{
		uint entity;
		do
			entity = phase == WorldPhase.View ? TakeViewEntityId() : TakeSimEntityId();
		while ( Exists( entity ) );

		entities[ entity ] = true;
		return entity;
	}

	public static bool IsViewEntity ( uint entity )
		=> entity >= VIEW_ENTITY_ID_START;

	uint TakeSimEntityId ()
	{
		if ( nextEntityId == 0 )
			nextEntityId = 1;

		if ( nextEntityId >= VIEW_ENTITY_ID_START )
			throw new InvalidOperationException( $"Out of sim entity ids: the sim range ends at { VIEW_ENTITY_ID_START }." );

		return nextEntityId++;
	}

	uint TakeViewEntityId ()
	{
		if ( nextViewEntityId < VIEW_ENTITY_ID_START )
			nextViewEntityId = VIEW_ENTITY_ID_START;

		if ( nextViewEntityId == uint.MaxValue )
			throw new InvalidOperationException( "Out of view entity ids." );

		return nextViewEntityId++;
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

        if ( Peek( entity, typeof( Undestroyable ) ) != null )
        {
            Log.Error( $"Attempted to destroy entity { GetName( entity ) } but it has an undestroyable component! Ignoring destroy!" );
            return;
        }

#if DEBUG
        if ( phase == WorldPhase.View )
        {
            foreach ( var ( type, componentDict ) in components )
            {
                if ( componentDict.ContainsKey( entity ) && SimComponentRules.IsSim( type ) )
                    throw new InvalidOperationException( $"The view destroyed { GetName( entity ) }, which has the sim component { type.Name }. The view never changes sim state." );
            }
        }
#endif

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

    	var name = Peek( entity, typeof( Name ) ) as Name;
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

    	var name = Peek( entity, typeof( Name ) ) as Name;
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
    	GuardWrite( entity, type, "add" );

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

    	GuardWrite( entity, type, "remove" );

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
   		GuardRead( type );

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
    	GuardRead( type );

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
        GuardRead( typeof( T ) );

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
#if DEBUG
    	if ( phase == WorldPhase.Sim )
    		throw new InvalidOperationException( "GetComponents reads view components too. Sim code reads sim components by type." );
#endif

    	var results = new List< Component >();
    	foreach ( var dict in components.Values )
    	{
    		if ( dict.ContainsKey( entity ) )
    			results.Add( dict[ entity ] );
    	}

    	return results;
    }

    /// <summary>Reads a component without the phase guard. For the world's own bookkeeping: filters, names, destroy.</summary>
    internal Component? Peek ( uint entity, Type type )
    	=> components.TryGetValue( type, out var componentDict ) && componentDict.TryGetValue( entity, out var component ) ? component : null;

    void GuardRead ( Type type )
    {
#if DEBUG
    	if ( phase == WorldPhase.Sim && !SimComponentRules.IsSim( type ) )
    		throw new InvalidOperationException( $"Sim code read the view component { type.Name }. The sim never reads view state." );
#endif
    }

    void GuardWrite ( uint entity, Type type, string action )
    {
    	if ( !SimComponentRules.IsSim( type ) )
    		return;

    	if ( IsViewEntity( entity ) )
    		throw new InvalidOperationException( $"Can't { action } the sim component { type.Name } on { GetName( entity ) }: it's a view entity." );

#if DEBUG
    	if ( phase == WorldPhase.View )
    		throw new InvalidOperationException( $"The view tried to { action } the sim component { type.Name } on { GetName( entity ) }. The view never changes sim state." );
#endif
    }
    #endregion


    #region Systems
	/// <summary>
	/// Starts a system after those already running. A sim system starts in the sim phase and a view or draw system in
	/// the view phase, so the same rules apply to its <see cref="ISystem.StartSystem"/> as to its updates.
	/// </summary>
	/// <exception cref="ArgumentException">The system is both sim and view.</exception>
	public void Start ( ISystem system )
    {
    	var systemPhase = PhaseOf( system );

    	var type = system.GetType();
    	if ( !systems.ContainsKey( type ) )
    		systems[ type ] = new();

    	systems[ type ].Add( system );

        if ( system is ISimSystem simSystem )
        	simSystemOrder.Add( simSystem );

        if ( system is IViewSystem viewSystem )
        	viewSystemOrder.Add( viewSystem );

        if ( system is IDrawSystem drawSystem )
        	drawSystemOrder.Add( drawSystem );

        simSystemSnapshot = null;
        viewSystemSnapshot = null;

        var previous = phase;
        phase = systemPhase;
        try
        {
        	system.StartSystem( this );
        }
        finally
        {
        	phase = previous;
        }
    }

    public void Stop ( ISystem system )
    {
    	var systemPhase = PhaseOf( system );

    	var type = system.GetType();
    	if ( system is ISimSystem simSystem )
    		simSystemOrder.Remove( simSystem );

    	if ( system is IViewSystem viewSystem )
    		viewSystemOrder.Remove( viewSystem );

    	if ( system is IDrawSystem drawSystem )
    		drawSystemOrder.Remove( drawSystem );

        simSystemSnapshot = null;
        viewSystemSnapshot = null;
    	systems[ type ].Remove( system );

    	if ( systems[ type ].Count == 0 )
    		systems.Remove( type );

        var previous = phase;
        phase = systemPhase;
        try
        {
        	system.StopSystem( this );
        }
        finally
        {
        	phase = previous;
        }
    }

    WorldPhase PhaseOf ( ISystem system )
    {
    	bool isSim = system is ISimSystem;
    	bool isView = system is IViewSystem || system is IDrawSystem;

    	if ( isSim && isView )
    		throw new ArgumentException( $"{ system.GetType().Name } is both a sim system and a view or draw system. A system is one or the other." );

    	return isSim ? WorldPhase.Sim : isView ? WorldPhase.View : phase;
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
    /// <summary>Points <paramref name="fromEntity"/>'s <typeparamref name="T"/> at <paramref name="toEntity"/>, keeping the relation's other data if it has one.</summary>
    public T SetRelation< T >( uint fromEntity, uint toEntity ) where T : Relation, new()
    {
    	var existing = Exists( fromEntity ) ? Peek( fromEntity, typeof( T ) ) as T : null;
    	if ( existing == null )
    		return Add( fromEntity, new T { relation = toEntity } );

    	if ( existing.relation == toEntity )
    		return existing;

    	GuardWrite( fromEntity, typeof( T ), "retarget" );
    	existing.relation = toEntity;
        structureVersion++;

        if ( !filtersByType.TryGetValue( typeof( T ), out var watchingFilters ) )
            return existing;

		foreach ( var filter in watchingFilters )
		{
			if ( !filter.Relates( typeof( T ) ) && !filter.NotRelates( typeof( T ) ) )
				continue;

			UpdateFilterWithRemove( filter, fromEntity );
			UpdateFilterWithAdd( filter, fromEntity );
		}

    	return existing;
    }

    /// <summary>Replaces <paramref name="fromEntity"/>'s <typeparamref name="T"/> with <paramref name="relation"/>, pointed at <paramref name="toEntity"/>.</summary>
    public T SetRelation< T >( uint fromEntity, uint toEntity, T relation ) where T : Relation, new()
    {
    	relation.relation = toEntity;
    	return Add( fromEntity, relation );
    }

    public void RemoveRelation< T > ( uint fromEntity, uint toEntity ) where T : Relation
    {
    	if ( !Exists( fromEntity ) || Peek( fromEntity, typeof( T ) ) is not T relation || relation.relation != toEntity )
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

    /// <summary><see cref="HasRelation(uint, uint, Type)"/> without the phase guard, for filters.</summary>
    internal bool Relates ( uint fromEntity, uint toEntity, Type type )
    	=> Exists( fromEntity ) && Peek( fromEntity, type ) is Relation relation && relation.relation == toEntity;

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
    	GuardRead( type );

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

    /// <summary>
    /// A <see cref="Command"/> waits for the next tick. Any other message goes to the current phase: sim messages last
    /// one tick and then move to the view's inbox, view messages last one frame.
    /// </summary>
    public T Send< T >( T message ) where T : Message 
    {
    	if ( message is Command command )
    	{
    		if ( phase == WorldPhase.Sim )
    			throw new InvalidOperationException( $"The sim sent the command { command.GetType().Name }. Commands come from the view; the sim sends ordinary messages." );

    		pendingCommands.Add( command );
    		return message;
    	}

    	AddMessage( CurrentMessages, message );
    	return message;
    }

    Dictionary< Type, List< Message > > CurrentMessages
    	=> phase == WorldPhase.Sim ? simMessages : messages;

    static void AddMessage ( Dictionary< Type, List< Message > > store, Message message )
    {
    	var type = message.GetType();
    	if ( !store.TryGetValue( type, out var list ) )
    	{
    		list = new List< Message >();
    		store[ type ] = list;
    	}

    	list.Add( message );
    }

    /// <summary>In the sim, this tick's messages and commands. In the view, this frame's messages and every sim message since the last frame.</summary>
    public List< T > Read< T >() where T : Message
    	=> CopyMessages< T >( CurrentMessages );

    public List< Message > Read ( System.Type messageType )
    {
    	if ( !CurrentMessages.TryGetValue( messageType, out var list ) )
    		return new List< Message >();

    	return list;
    }

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

    public T ReadSingle< T > () where T : Message
    	=> FirstMessage< T >( CurrentMessages );

    public Message ReadSingle ( System.Type type )
    {
    	var results = Read( type );
    	return results.Count == 0 ? null : results[ 0 ];
    }

    public void Destroy ( Message message )
	{
		if ( CurrentMessages.TryGetValue( message.GetType(), out var list ) )
			list.Remove( message );
	}
	#endregion


	#region Filters
    /// <summary>
    /// Builds the filter's set from scratch and keeps it up to date. The filter remembers the phase it started in, and
    /// its callbacks always run in that phase, so a view filter's callbacks follow view rules even when a tick fires them.
    /// </summary>
    public void Start ( Filter filter )
    {    	
    	if ( filter.isBeingModified )
    		Log.Error( "Filter is being recursively modified! This is not allowed." );

    	GuardSimQuery( filter.watchedTypes );

    	filter.isBeingModified = true;
    	filter.phase = phase;

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
	    	InvokeFilterCallback( filter, filter.onAdd, entity );

	    filter.isBeingModified = false;
    }

    public void Stop ( Filter filter )
    {
    	if ( filter.isBeingModified )
    		Log.Error( "Filter is being recursively modified! This is not allowed." );

    	filter.isBeingModified = true;

    	foreach ( var entity in filter.entities)
	        InvokeFilterCallback( filter, filter.onRemove, entity );

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
    	GuardSimQuery( filterBuilder.watchedTypes );

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
    	GuardSimQuery( filterBuilder.watchedTypes );

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

    void GuardSimQuery ( IEnumerable< Type > types )
    {
#if DEBUG
    	if ( phase != WorldPhase.Sim )
    		return;

    	foreach ( var type in types )
    	{
    		if ( !SimComponentRules.IsSim( type ) )
    			throw new InvalidOperationException( $"Sim code queried the view component { type.Name }. The sim never reads view state." );
    	}
#endif
    }

    void InvokeFilterCallback ( Filter filter, Action< uint > callback, uint entity )
    {
    	if ( callback == null )
    		return;

    	var previous = phase;
    	if ( filter.phase != WorldPhase.Default )
    		phase = filter.phase;

    	try
    	{
    		callback( entity );
    	}
    	finally
    	{
    		phase = previous;
    	}
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

	    var toAdd = new HashSet< uint >( newEntities );
	    toAdd.ExceptWith( filter.entities );

	    var toRemove = new HashSet< uint >( filter.entities );
	    toRemove.ExceptWith( newEntities );

	    filter.entities = newEntities;

	    foreach ( var entity in toRemove)
	        InvokeFilterCallback( filter, filter.onRemove, entity );

	    foreach ( var entity in toAdd )
	        InvokeFilterCallback( filter, filter.onAdd, entity );

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
    	InvokeFilterCallback( filter, filter.onAdd, addedEntity );

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
    	InvokeFilterCallback( filter, filter.onRemove, removedEntity );

    	filter.isBeingModified = false;
    }
    #endregion


    #region Phases
    /// <summary>
    /// Runs one fixed tick: increments <see cref="tick"/>, applies pending commands stamped with it, and runs sim
    /// systems in start order. The tick's sim messages then move to the view's inbox.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from inside a tick, a frame or a view scope.</exception>
    public void FixedTick ()
    {
    	if ( phase != WorldPhase.Default )
    		throw new InvalidOperationException( $"FixedTick can't run inside the { phase } phase." );

    	phase = WorldPhase.Sim;
    	try
    	{
    		tick++;

    		foreach ( var command in pendingCommands )
    		{
    			command.tick = tick;
    			AddMessage( simMessages, command );
    		}
    		pendingCommands.Clear();

    		simSystemSnapshot ??= simSystemOrder.ToArray();
    		foreach ( var system in simSystemSnapshot )
    			system.FixedUpdate( this );
    	}
    	finally
    	{
    		MoveSimMessagesToInbox();
    		phase = WorldPhase.Default;
    	}
    }

    void MoveSimMessagesToInbox ()
    {
    	foreach ( var ( type, list ) in simMessages )
    	{
    		if ( messages.TryGetValue( type, out var inbox ) )
    			inbox.AddRange( list );
    		else
    			messages[ type ] = list;
    	}

    	simMessages = new Dictionary< Type, List< Message > >();
    }

    /// <summary>
    /// Runs one frame of the view: advances the clocks (if a <see cref="TimeSystem"/> is running), then runs view
    /// systems in start order. This frame's view messages and the sim messages in the inbox are then cleared.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from inside a tick, a frame or a view scope.</exception>
    public void Update ( FrameTime time )
    {
    	if ( phase != WorldPhase.Default )
    		throw new InvalidOperationException( $"Update can't run inside the { phase } phase." );

    	try
    	{
    		using ( EnterViewPhase() )
    		{
    			if ( TryGet< TimeSystem >( out var timeSystem ) )
    				timeSystem.AdvanceFrame( time );

    			viewSystemSnapshot ??= viewSystemOrder.ToArray();
    			foreach ( var system in viewSystemSnapshot )
    				system.Update( this, time );
    		}
    	}
    	finally
    	{
    		messages.Clear();
    	}
    }

    public void Draw ( SpriteBatch batch )
    {
    	using ( EnterViewPhase() )
    	{
    		foreach ( var system in drawSystemOrder )
    			system.DrawSystem( this, batch );
    	}
    }

    /// <summary>
    /// Runs view code outside <see cref="Update"/> and <see cref="Draw"/>, such as input handlers, under view rules:
    /// new entities get view ids and sim components can't change. Dispose to leave.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called from inside a tick.</exception>
    public ViewPhaseScope EnterViewPhase ()
    {
    	if ( phase == WorldPhase.Sim )
    		throw new InvalidOperationException( "The view phase can't start inside a tick." );

    	var previous = phase;
    	phase = WorldPhase.View;

#if DEBUG
    	return new ViewPhaseScope( this, previous, Checksum() );
#else
    	return new ViewPhaseScope( this, previous, 0 );
#endif
    }

    internal void ExitViewPhase ( WorldPhase previous, ulong checksumBefore )
    {
    	phase = previous;

#if DEBUG
    	if ( Checksum() != checksumBefore )
    		throw new InvalidOperationException( "Sim state changed during the view phase. The view reads sim components but never writes them." );
#endif
    }

    /// <summary>Rounds up, so any nonzero wait lasts at least one tick.</summary>
    public long TicksFromMilliseconds ( int milliseconds )
    {
    	if ( milliseconds < 0 )
    		throw new ArgumentOutOfRangeException( nameof( milliseconds ), milliseconds, "Must not be negative." );

    	return ( ( long )milliseconds * ticksPerSecond + 999 ) / 1000;
    }

    public long TicksFromSeconds ( int seconds )
    {
    	if ( seconds < 0 )
    		throw new ArgumentOutOfRangeException( nameof( seconds ), seconds, "Must not be negative." );

    	return ( long )seconds * ticksPerSecond;
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

public readonly struct ViewPhaseScope : IDisposable
{
	readonly World world;
	readonly WorldPhase previous;
	readonly ulong checksumBefore;

	internal ViewPhaseScope ( World world, WorldPhase previous, ulong checksumBefore )
	{
		this.world = world;
		this.previous = previous;
		this.checksumBefore = checksumBefore;
	}

	public void Dispose ()
		=> world?.ExitViewPhase( previous, checksumBefore );
}
