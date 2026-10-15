using Engine;
using System.Collections.Generic;

namespace MintECS;

public class WorldManipulator
{
	public World world { get; protected set; }

	#region Entities

	protected uint Create ()
		=> world.Create();

	protected uint Create ( string name, params Component[] components )
		=> world.Create( name, components );

	protected uint Create ( params Component[] components )
		=> world.Create( components );

	protected void Destroy ( uint entity )
		=> world.Destroy( entity );

	protected bool Exists ( uint entity )
		=> world.Exists( entity );

	protected string GetName ( uint entity )
		=> world.GetName( entity );

	protected string GetBasicName ( uint entity )
		=> world.GetBasicName( entity );

	#endregion


	#region Components

	protected T Add< T >( uint entity ) where T : Component, new()
		=> world.Add< T >( entity );

	protected Component Add ( uint entity, System.Type type )
		=> world.Add( entity, type );

	protected T Add< T >( uint entity, T component ) where T : Component
		=> world.Add( entity, component );

	protected void AddBatch ( uint entity, params Component[] components )
		=> world.AddBatch( entity, components );

	protected void Remove< T >( uint entity ) where T : Component
		=> world.Remove< T >( entity );

	protected void Remove( uint entity, System.Type type )
		=> world.Remove( entity, type );

	protected bool Has< T >( uint entity )  where T : Component 
		=> world.Has< T >( entity );

	protected bool Has ( uint entity, System.Type type )
		=> world.Has( entity, type );

	protected T Get< T >( uint entity )  where T : Component 
		=> world.Get< T >( entity );

    protected bool TryGet< T > ( uint entity, out T component ) where T : Component
        => world.TryGet( entity, out component );
    
	protected Component Get ( uint entity, System.Type type )
		=> world.Get( entity, type );

	protected T GetOrAdd< T > ( uint entity ) where T : Component, new()
		=> world.GetOrAdd< T >( entity );

	protected Component GetOrAdd ( uint entity, System.Type type )
		=> world.GetOrAdd( entity, type );

	#endregion


	#region Relations

	protected T SetRelation< T >( uint fromEntity, uint toEntity ) where T : Relation, new()
		=> world.SetRelation< T >( fromEntity, toEntity );

	protected T SetRelation< T >( uint fromEntity, uint toEntity, T relation ) where T : Relation, new()
		=> world.SetRelation< T >( fromEntity, toEntity, relation );

	protected void RemoveRelation< T > ( uint fromEntity, uint toEntity ) where T : Relation
		=> world.RemoveRelation< T >( fromEntity, toEntity );

	protected bool HasRelation< T > ( uint fromEntity, uint toEntity ) where T : Relation
		=> world.HasRelation< T >( fromEntity, toEntity );

	protected bool HasRelation ( uint fromEntity, uint toEntity, System.Type type )
		=> world.HasRelation( fromEntity, toEntity, type );

	protected T GetRelation< T >( uint fromEntity, uint toEntity ) where T : Relation
		=> world.GetRelation< T >( fromEntity, toEntity );

	protected List< uint > GetEntities< T > ( uint entity ) where T : Relation
		=> world.GetEntities< T >( entity );

	protected Dictionary< uint, T > GetRelations< T >( uint entity ) where T : Relation
		=> world.GetRelations< T >( entity );

	protected uint GetRelated< T >( uint fromEntity ) where T : Relation
		=> world.GetRelated< T >( fromEntity );

	#endregion


	#region Systems

	protected void Start ( ISystem system )
		=> world.Start( system );

	protected void Stop ( ISystem system )
		=> world.Stop( system );

	protected T Get< T > () where T : ISystem
		=> world.Get< T >();

    protected bool TryGet< T > ( out T result ) where T : ISystem
        => world.TryGet< T >( out result );

	#endregion


	#region Messages

	protected T Send< T > () where T: Message, new ()
		=> world.Send< T >();

	protected T Send< T >( T message ) where T : Message 
		=> world.Send< T >( message );

	protected List< T > Read< T >() where T : Message
		=> world.Read< T >();

	protected List< Message > Read ( System.Type messageType )
		=> world.Read( messageType );

	protected List< T > ReadOld< T >() where T : Message
		=> world.ReadOld< T >();

	protected List< Message > ReadOld ( System.Type messageType )
		=> world.ReadOld( messageType );

	protected T ReadSingle< T >() where T : Message
		=> world.ReadSingle< T >();

	protected Message ReadSingle ( System.Type type )
		=> world.ReadSingle( type );

	protected T ReadSingleOld< T >() where T : Message
		=> world.ReadSingleOld< T >();

    protected Message ReadSingleOld ( System.Type type )
    	=> world.ReadSingleOld( type );

	protected void Destroy ( Message message )
		=> world.Destroy( message );
    
    protected void DestroyOld ( Message message )
        => world.DestroyOld( message );

	#endregion


	#region Filters

	protected void Start ( Filter filter )
		=> world.Start( filter );

	protected void Stop ( Filter filter )
		=> world.Stop( filter );

	protected void GetEntitiesNonAlloc ( FilterBuilder filterBuilder, HashSet< uint > results )
		=> world.GetEntitiesNonAlloc( filterBuilder, results );
    
    protected void GetEntitiesNonAlloc ( FilterBuilder filterBuilder, List< uint > results )
        => world.GetEntitiesNonAlloc( filterBuilder, results );

	protected uint GetSingleEntity ( FilterBuilder filterBuilder )
		=> world.GetSingleEntity( filterBuilder );

	#endregion
}