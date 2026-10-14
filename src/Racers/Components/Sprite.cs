using Engine;
using System.Numerics;

namespace MintECS;

public class Sprite : Component
{
	public string path;
	public Vector2 pivot = Vector2.Zero;
	public Color color = Color.White;
}