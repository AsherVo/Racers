using Engine;
using System.Numerics;

namespace MintECS;

public class SortOrder : Component
{
	public int value;

	public static implicit operator int ( SortOrder s )
		=> s.value;
}