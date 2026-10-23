using System.Collections;

namespace MintECS;

/// <summary>
/// A set of entity ids kept in ascending order. Iterating it visits entities in the same order on every run, whatever
/// order they were added in and after a load, which is what the sim needs (Rule 6).
/// </summary>
public sealed class SortedEntities : IReadOnlyList< uint >
{
	readonly List< uint > ids = new();

	public int Count => ids.Count;

	public uint this[ int index ] => ids[ index ];

	public bool Contains ( uint entity )
		=> ids.BinarySearch( entity ) >= 0;

	/// <summary>Doesn't allocate. Changing the set while iterating it throws.</summary>
	public List< uint >.Enumerator GetEnumerator ()
		=> ids.GetEnumerator();

	IEnumerator< uint > IEnumerable< uint >.GetEnumerator ()
		=> GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator ()
		=> GetEnumerator();

	/// <summary>Returns false if the entity was already in the set.</summary>
	internal bool Add ( uint entity )
	{
		int index = ids.BinarySearch( entity );
		if ( index >= 0 )
			return false;

		ids.Insert( ~index, entity );
		return true;
	}

	/// <summary>Returns false if the entity wasn't in the set.</summary>
	internal bool Remove ( uint entity )
	{
		int index = ids.BinarySearch( entity );
		if ( index < 0 )
			return false;

		ids.RemoveAt( index );
		return true;
	}

	internal void Clear ()
		=> ids.Clear();
}
