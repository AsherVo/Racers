using Engine;
using MintECS;

namespace Racers;

public class Resource : Relation, ISimComponent
{
	public int value;

	public static implicit operator int ( Resource r )
		=> r.value;
}
