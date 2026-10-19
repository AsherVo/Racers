using Engine;
using MintECS;

namespace Racers;

public class DrawColor : Component
{
	public Color value = Color.White;

	public static implicit operator Color ( DrawColor c )
		=> c.value;
}