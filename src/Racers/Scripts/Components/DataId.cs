using Engine;
using MintECS;

namespace Racers;

public class DataId : Component, ISimComponent
{
	public string id;

	public static implicit operator string ( DataId id )
		=> id.id;
}