namespace MintECS;

/// <summary>Deterministic simulation. Runs once per fixed tick in <see cref="World.FixedTick"/>.</summary>
public interface ISimSystem : ISystem
{
	public void FixedUpdate ( World world );
}
