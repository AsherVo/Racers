using System.Diagnostics.CodeAnalysis;
using Engine;

namespace MintECS;

public partial class World
{
	const string TRIM_JUSTIFICATION = "Component types live in the game assembly, which hosts root.";

	static Dictionary< string, Type >? ComponentTypesByName;

	/// <summary>
	/// Everything that persists, as YAML: the counters, pending commands, and every live entity with its components,
	/// by ascending id and then type name. <see cref="Load"/> reads it back. Call between ticks and frames; messages
	/// and systems aren't saved.
	/// </summary>
	public string Save ()
		=> Yaml.Serialize( ToSaveData( simOnly: false ) );

	/// <summary>
	/// Only what <see cref="Checksum"/> covers: the tick, the sim id counter, and every sim component on a sim entity.
	/// Two worlds with equal checksums produce identical text, so diffing two of these finds where runs diverged.
	/// </summary>
	public string SaveSimState ()
		=> Yaml.Serialize( ToSaveData( simOnly: true ) );

	/// <summary>
	/// Builds a new world from <see cref="Save"/>'s YAML. No systems are running: start them afterwards, and they
	/// rebuild their caches from the loaded state like after any other start.
	/// </summary>
	/// <exception cref="YamlException">The text isn't a saved world.</exception>
	/// <exception cref="InvalidDataException">The world it describes breaks a world rule, such as a sim component on a view entity.</exception>
	public static World Load ( string yaml, string? source = null )
	{
		var data = Yaml.Deserialize< WorldSaveData >( yaml, source: source );
		if ( data.ticksPerSecond <= 0 )
			throw new InvalidDataException( $"{ source ?? "World" }: ticksPerSecond must be positive, not { data.ticksPerSecond }." );

		var world = new World( data.ticksPerSecond )
		{
			tick = data.tick,
			nextEntityId = data.nextEntityId,
			nextViewEntityId = data.nextViewEntityId,
			pendingCommands = data.pendingCommands,
		};

		foreach ( var entityData in data.entities )
			world.LoadEntity( entityData, source );

		return world;
	}

	WorldSaveData ToSaveData ( bool simOnly )
	{
		var data = new WorldSaveData
		{
			ticksPerSecond = ticksPerSecond,
			tick = tick,
			nextEntityId = nextEntityId,
			nextViewEntityId = simOnly ? 0 : nextViewEntityId,
			pendingCommands = simOnly ? new List< Command >() : pendingCommands,
		};

		var componentTypes = ComponentTypesInOrder()
			.Where( type => !simOnly || SimComponentRules.IsSim( type ) )
			.OrderBy( type => type.Name, StringComparer.Ordinal )
			.ToArray();

		foreach ( uint entity in LiveEntitiesInOrder() )
		{
			if ( simOnly && IsViewEntity( entity ) )
				continue;

			var componentMap = new YamlMap();
			foreach ( var type in componentTypes )
			{
				if ( components[ type ].TryGetValue( entity, out var component ) )
					componentMap.Add( type.Name, WriteComponent( component ) );
			}

			if ( simOnly && componentMap.Count == 0 )
				continue;

			data.entities.Add( new EntitySaveData { id = entity, components = componentMap } );
		}

		return data;
	}

	void LoadEntity ( EntitySaveData data, string? source )
	{
		string where = $"{ source ?? "World" }: entity { data.id }";
		if ( data.id == 0 )
			throw new InvalidDataException( $"{ where }: 0 means no entity and can't be loaded." );

		if ( Exists( data.id ) )
			throw new InvalidDataException( $"{ where } is listed twice." );

		var counter = IsViewEntity( data.id ) ? nextViewEntityId : nextEntityId;
		if ( data.id >= counter )
			throw new InvalidDataException( $"{ where } is at or past its id counter ({ counter }), so a later Create would reuse it." );

		entities[ data.id ] = true;
		foreach ( var ( keyNode, valueNode ) in data.components )
		{
			string typeName = ( keyNode as YamlScalar )?.value ?? "";
			if ( !ComponentTypes().TryGetValue( typeName, out var type ) )
				throw new InvalidDataException( $"{ where }: unknown component '{ typeName }'." );

			if ( IsViewEntity( data.id ) && SimComponentRules.IsSim( type ) )
				throw new InvalidDataException( $"{ where } is a view entity, but has the sim component { typeName }." );

			ComponentsOfType( type )[ data.id ] = ReadComponent( valueNode, type, source );
		}
	}

	[UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = TRIM_JUSTIFICATION )]
	static YamlNode WriteComponent ( Component component )
		=> Yaml.ToNode( component, component.GetType() );

	[UnconditionalSuppressMessage( "Trimming", "IL2067", Justification = TRIM_JUSTIFICATION )]
	static Component ReadComponent ( YamlNode node, Type type, string? source )
		=> ( Component )Yaml.Deserialize( node, type, source: source );

	/// <summary>Every concrete component type in the game assembly, by its short name, which is how saves name them.</summary>
	[UnconditionalSuppressMessage( "Trimming", "IL2026", Justification = TRIM_JUSTIFICATION )]
	static Dictionary< string, Type > ComponentTypes ()
	{
		if ( ComponentTypesByName != null )
			return ComponentTypesByName;

		var byName = new Dictionary< string, Type >( StringComparer.Ordinal );
		foreach ( var type in typeof( World ).Assembly.GetTypes() )
		{
			if ( type.IsAbstract || !typeof( Component ).IsAssignableFrom( type ) )
				continue;

			if ( byName.TryGetValue( type.Name, out var existing ) )
				throw new InvalidOperationException( $"{ existing.FullName } and { type.FullName } are both named { type.Name }. Saves name components by type name, so it must be unique." );

			byName[ type.Name ] = type;
		}

		ComponentTypesByName = byName;
		return byName;
	}
}

sealed class WorldSaveData
{
	public int ticksPerSecond;
	public long tick;
	public uint nextEntityId = 1;
	[YamlIgnore( condition = YamlIgnoreCondition.WhenDefault )] public uint nextViewEntityId = World.VIEW_ENTITY_ID_START;
	public List< Command > pendingCommands = new();
	public List< EntitySaveData > entities = new();
}

sealed class EntitySaveData
{
	public uint id;
	public YamlMap components = new();
}
