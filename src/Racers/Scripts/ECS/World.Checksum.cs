using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace MintECS;

public partial class World
{
	const ulong FNV_OFFSET_BASIS = 14695981039346656037;
	const ulong FNV_PRIME = 1099511628211;

	static readonly ConcurrentDictionary< Type, FieldInfo[] > ChecksumFields = new();

	/// <summary>
	/// A 64-bit FNV-1a hash of the simulation: <see cref="tick"/>, <see cref="nextEntityId"/>, and every
	/// <see cref="ISimComponent"/>, by entity id and then type name. Equal on every platform for equal sim state.
	/// </summary>
	public ulong Checksum ()
	{
		var simComponents = new List< ( uint entity, string typeName, Component component ) >();
		foreach ( var ( type, componentDict ) in components )
		{
			if ( !SimComponentRules.IsSim( type ) )
				continue;

			string typeName = type.FullName!;
			foreach ( var ( entity, component ) in componentDict )
				simComponents.Add( ( entity, typeName, component ) );
		}

		simComponents.Sort( ( a, b ) => a.entity != b.entity ? a.entity.CompareTo( b.entity ) : string.CompareOrdinal( a.typeName, b.typeName ) );

		ulong hash = FNV_OFFSET_BASIS;
		hash = HashInteger( hash, ( ulong )tick );
		hash = HashInteger( hash, nextEntityId );
		foreach ( var ( entity, typeName, component ) in simComponents )
		{
			hash = HashInteger( hash, entity );
			hash = HashString( hash, typeName );
			hash = HashFields( hash, component );
		}

		return hash;
	}

	static ulong HashFields ( ulong hash, object value )
	{
		var fields = ChecksumFields.GetOrAdd( value.GetType(), type => SimComponentRules.OrderedFields( type ).ToArray() );
		foreach ( var field in fields )
			hash = HashValue( hash, field.GetValue( value ) );

		return hash;
	}

	static ulong HashValue ( ulong hash, object? value )
	{
		switch ( value )
		{
			case null:
				return HashByte( hash, 0 );
			case string text:
				return HashString( HashByte( hash, 1 ), text );
			case bool flag:
				return HashByte( hash, flag ? ( byte )1 : ( byte )0 );
			case char character:
				return HashInteger( hash, character );
			case Enum when Enum.GetUnderlyingType( value.GetType() ) == typeof( ulong ):
				return HashInteger( hash, Convert.ToUInt64( value ) );
			case Enum:
				return HashInteger( hash, ( ulong )Convert.ToInt64( value ) );
			case byte or sbyte or short or ushort or int or uint or long:
				return HashInteger( hash, ( ulong )Convert.ToInt64( value ) );
			case ulong number:
				return HashInteger( hash, number );
			case decimal number:
				foreach ( int part in decimal.GetBits( number ) )
					hash = HashInteger( hash, ( uint )part );
				return hash;
			case float or double or Half:
				throw new InvalidOperationException( $"Can't checksum a { value.GetType().Name }. Sim state must not hold floats." );
			case IList list:
				hash = HashInteger( HashByte( hash, 2 ), ( ulong )list.Count );
				foreach ( var item in list )
					hash = HashValue( hash, item );
				return hash;
			default:
				return HashFields( HashByte( hash, 3 ), value );
		}
	}

	static ulong HashString ( ulong hash, string text )
	{
		hash = HashInteger( hash, ( ulong )text.Length );
		foreach ( char character in text )
		{
			hash = HashByte( hash, ( byte )character );
			hash = HashByte( hash, ( byte )( character >> 8 ) );
		}

		return hash;
	}

	static ulong HashInteger ( ulong hash, ulong value )
	{
		for ( int shift = 0; shift < 64; shift += 8 )
			hash = HashByte( hash, ( byte )( value >> shift ) );

		return hash;
	}

	static ulong HashByte ( ulong hash, byte value )
		=> ( hash ^ value ) * FNV_PRIME;
}
