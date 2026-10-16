using Engine;

namespace MintECS;

public class Position : Component
{
	public Vector2 value;

	public static implicit operator Vector2 ( Position p )
		=> p.value;
}