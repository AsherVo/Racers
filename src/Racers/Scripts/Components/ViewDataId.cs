using Engine;
using MintECS;

namespace Racers;

public class ViewDataId : Component
{
	public string id;

	public static implicit operator string ( ViewDataId id )
		=> id.id;
} 