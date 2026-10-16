using Engine;

namespace MintECS;

public class DataId : Component
{
	public string id;

	public static implicit operator string ( DataId id )
		=> id.id;
}