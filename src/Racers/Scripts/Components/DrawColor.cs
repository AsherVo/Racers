using Engine;

namespace MintECS;

public class DrawColor : Component
{
	public Color value = Color.White;

	public static implicit operator Color ( DrawColor c )
		=> c.value;
}