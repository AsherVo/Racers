using Engine;
using MintECS;

namespace Racers;

public class Rect : Component
{
	public float width;
	public float height;
	public Vector2 pivot = Vector2.Zero;
}