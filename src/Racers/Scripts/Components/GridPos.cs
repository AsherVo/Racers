using Engine;
using MintECS;

namespace Racers;

public class GridPos : Component, ISimComponent
{
	public const int PROGRESS_PER_CELL = 1000;

	public Vector2Int cell;
	public int progress;
}
