using Engine;
using System.Collections.Generic;

namespace MintECS;

public interface IDrawSystem
{
	public void DrawSystem ( World world, SpriteBatch batch );
}