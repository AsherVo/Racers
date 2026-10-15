using System.Numerics;

namespace MintECS;

public class Rect : Component
{
	public float width;
	public float height;
	public Vector2 pivot = Vector2.Zero;
}