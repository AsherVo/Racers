using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace MintECS;

/// <summary>
/// Classifies component types as sim or view, and checks that sim components only hold data that behaves the same on
/// every platform: integers, bools, strings, enums, and arrays, lists and structs of those.
/// </summary>
public static class SimComponentRules
{
	const BindingFlags DECLARED_FIELDS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

	static readonly ConcurrentDictionary< Type, bool > IsSimByType = new();

	/// <summary>Whether a component type is sim state. The first time a sim type is seen, its fields are checked.</summary>
	/// <exception cref="InvalidOperationException">The type is an <see cref="ISimComponent"/> with a field that breaks the rules.</exception>
	public static bool IsSim ( Type componentType )
		=> IsSimByType.TryGetValue( componentType, out bool isSim ) ? isSim : IsSimByType.GetOrAdd( componentType, Classify );

	/// <summary>Checks every <see cref="ISimComponent"/> in an assembly at once, so a bad field fails at startup instead of on first use.</summary>
	/// <exception cref="InvalidOperationException">Lists every field that breaks the rules.</exception>
	[UnconditionalSuppressMessage( "Trimming", "IL2026", Justification = "Data assemblies are rooted (YamlDataAssemblies in hosts/Directory.Build.targets), so none of their types are trimmed." )]
	public static void ValidateAssembly ( Assembly assembly )
	{
		var problems = new List< string >();
		foreach ( var type in assembly.GetTypes().Where( IsSimComponentType ).OrderBy( type => type.FullName, StringComparer.Ordinal ) )
			problems.AddRange( FindProblems( type ) );

		if ( problems.Count > 0 )
			throw new InvalidOperationException( $"Sim components must be deterministic:\n{ string.Join( "\n", problems ) }" );
	}

	public static List< string > FindProblems ( Type componentType )
	{
		var problems = new List< string >();
		CheckFields( componentType, componentType.Name, problems, new HashSet< Type >() );
		return problems;
	}

	/// <summary>Instance fields, base class first, each class's sorted by name, so the order is the same on every runtime.</summary>
	[UnconditionalSuppressMessage( "Trimming", "IL2075", Justification = "Components live in data assemblies, which are rooted (YamlDataAssemblies in hosts/Directory.Build.targets), so their fields are kept." )]
	public static IEnumerable< FieldInfo > OrderedFields ( Type type )
	{
		var chain = new Stack< Type >();
		for ( var t = type; t != null && t != typeof( object ) && t != typeof( ValueType ); t = t.BaseType )
			chain.Push( t );

		foreach ( var t in chain )
		{
			foreach ( var field in t.GetFields( DECLARED_FIELDS ).OrderBy( field => field.Name, StringComparer.Ordinal ) )
				yield return field;
		}
	}

	public static string FieldName ( FieldInfo field )
	{
		string name = field.Name;
		int close = name.IndexOf( '>' );
		return name.StartsWith( '<' ) && close > 1 ? name[ 1..close ] : name;
	}

	static bool IsSimComponentType ( Type type )
		=> typeof( Component ).IsAssignableFrom( type ) && typeof( ISimComponent ).IsAssignableFrom( type );

	static bool Classify ( Type componentType )
	{
		if ( !typeof( ISimComponent ).IsAssignableFrom( componentType ) )
			return false;

		var problems = FindProblems( componentType );
		if ( problems.Count > 0 )
			throw new InvalidOperationException( $"Sim components must be deterministic:\n{ string.Join( "\n", problems ) }" );

		return true;
	}

	static void CheckFields ( Type type, string path, List< string > problems, HashSet< Type > visited )
	{
		if ( !visited.Add( type ) )
			return;

		foreach ( var field in OrderedFields( type ) )
			CheckType( field.FieldType, $"{ path }.{ FieldName( field ) }", problems, visited );
	}

	static void CheckType ( Type type, string path, List< string > problems, HashSet< Type > visited )
	{
		if ( type == typeof( float ) || type == typeof( double ) || type == typeof( Half ) )
		{
			problems.Add( $"{ path } is a { type.Name }. Floats round differently across platforms; use int, long or Fixed." );
			return;
		}

		if ( type.IsPrimitive || type.IsEnum || type == typeof( string ) || type == typeof( decimal ) )
			return;

		var nullable = Nullable.GetUnderlyingType( type );
		if ( nullable != null )
		{
			CheckType( nullable, path, problems, visited );
			return;
		}

		if ( type.IsArray )
		{
			CheckType( type.GetElementType()!, $"{ path }[]", problems, visited );
			return;
		}

		if ( type.IsGenericType )
		{
			var definition = type.GetGenericTypeDefinition();
			if ( definition == typeof( List<> ) )
			{
				CheckType( type.GetGenericArguments()[ 0 ], $"{ path }[]", problems, visited );
				return;
			}

			if ( definition == typeof( Dictionary<,> ) || definition == typeof( HashSet<> ) )
			{
				problems.Add( $"{ path } is a { definition.Name[ ..definition.Name.IndexOf( '`' ) ] }, which enumerates in hash order. Use a sorted list or an array." );
				return;
			}
		}

		if ( typeof( Component ).IsAssignableFrom( type ) && !typeof( ISimComponent ).IsAssignableFrom( type ) )
		{
			problems.Add( $"{ path } references the view component { type.Name }. The sim never reads view state." );
			return;
		}

		if ( type == typeof( object ) || type.IsInterface || typeof( Delegate ).IsAssignableFrom( type ) || type.IsPointer )
		{
			problems.Add( $"{ path } is a { type.Name }, which could hold anything. Use a concrete data type." );
			return;
		}

		if ( type.Namespace == "System" || type.Namespace?.StartsWith( "System.", StringComparison.Ordinal ) == true )
		{
			problems.Add( $"{ path } is a { type.FullName }, which sim components don't support." );
			return;
		}

		CheckFields( type, path, problems, visited );
	}
}
