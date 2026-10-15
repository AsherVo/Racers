using Engine;
using System.Numerics;

namespace MintECS;

public class DataId : Component
{
	public string id;

	public static implicit operator string ( DataId id )
		=> id.id;
}