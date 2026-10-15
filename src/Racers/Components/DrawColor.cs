using Engine;
using System.Numerics;

namespace MintECS;

public class DrawColor : Component
{
	public Color color = Color.White;

	public static implicit operator Color ( DrawColor c )
		=> c.color;
}