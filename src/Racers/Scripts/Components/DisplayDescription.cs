using Engine;
using MintECS;

namespace Racers;

public class DisplayDescription : Component
{
	public string value;

	public static implicit operator string ( DisplayDescription d )
		=> d.value;
}