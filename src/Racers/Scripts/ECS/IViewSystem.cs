using Engine;

namespace MintECS;

/// <summary>Presentation and input. Runs once per frame in <see cref="World.Update"/>.</summary>
public interface IViewSystem : ISystem
{
	public void Update ( World world, FrameTime time );
}
