using Engine;
using Racers;
using System.Numerics;

namespace MintECS;

public class RenderSystem : WorldManipulator, ISystem, IDrawSystem
{
	Filter sprites;

	public void StartSystem ( World world )
	{
		this.world = world;

		sprites = new FilterBuilder()
			.Include< Sprite >()
			.Include< Position >()
			.ToFilter();

		Start( sprites );
	}

	public void StopSystem ( World world )
	{
		Stop( sprites );

		this.world = null;
	}

	public void TickSystem ( World world )
	{
		
	}

	public void DrawSystem ( World world, SpriteBatch batch )
	{
		foreach ( var spriteEntity in sprites.entities )
		{
			var sprite = Get< Sprite >( spriteEntity );
			var position = Get< Position >( spriteEntity );

			var texture = Toolbox.I.load.Texture( sprite.path );
			var pivot = new Vector2( texture.width * sprite.pivot.X, texture.height * ( 1f - sprite.pivot.Y ) );
			batch.Draw( texture, position, null, sprite.color, 0f, pivot, Vector2.One );
		}
	}
}