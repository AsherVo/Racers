namespace MintECS;

public enum WorldPhase
{
	/// <summary>Outside any tick or frame, such as during loading. Creates sim-range entities.</summary>
	Default,

	/// <summary>Inside <see cref="World.FixedTick"/>. Reads only sim components and sim messages.</summary>
	Sim,

	/// <summary>Inside <see cref="World.Update"/>, <see cref="World.Draw"/> or <see cref="World.EnterViewPhase"/>. Creates view-range entities.</summary>
	View,
}
