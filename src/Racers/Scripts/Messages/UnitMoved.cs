using Engine;
using MintECS;

namespace Racers;

public class UnitMoved : Message
{
	public uint unit;
	public Vector2Int from;
	public Vector2Int to;
}