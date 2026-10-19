using Engine;
using MintECS;

namespace Racers;

public class SortOrder : Component
{
	public int value;

	public static implicit operator int ( SortOrder s )
		=> s.value;
}