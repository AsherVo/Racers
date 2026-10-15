using Engine;
using System.Numerics;

namespace MintECS;

public class DrawText : Component
{
	public string text = "";
	public string font;
	public int fontSize;
}