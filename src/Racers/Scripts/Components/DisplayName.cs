using Engine;
using MintECS;

namespace Racers;

public class DisplayName : Component
{
	public string value;

	public static implicit operator string ( DisplayName d )
		=> d.value;
}