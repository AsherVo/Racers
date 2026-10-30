using Engine;
using MintECS;
using Racers;

using RectangleF = System.Drawing.RectangleF;
using System.Collections.Generic;

namespace Racers;

public class RenderSystem : WorldManipulator, IViewSystem, IDrawSystem
{
	Filter canvases;
	Dictionary< uint, Filter > canvasFilters;

	Filter sprites;

	Filter rives;
	Dictionary< uint, RiveInstance > riveInstances;

	public void StartSystem ( World world )
	{
		this.world = world;

		canvasFilters = new();

		canvases = new FilterBuilder()
			.Include< Canvas >()
			.Exclude< Hidden >()
			.ToFilter();

		canvases.onAdd += OnAddCanvas;
		canvases.onRemove += OnRemoveCanvas;
		Start( canvases );

		sprites = new FilterBuilder()
			.Include< DrawSprite >()
			.Include< Rect >()
			.ToFilter();

		sprites.onAdd += OnAddSprite;
		Start( sprites );

		riveInstances = new();

		rives = new FilterBuilder()
			.Include< DrawRive >()
			.Include< Rect >()
			.ToFilter();

		rives.onAdd += OnAddRive;
		rives.onRemove += OnRemoveRive;
		Start( rives );
	}

	public void StopSystem ( World world )
	{
		Stop( rives );
		rives.onAdd -= OnAddRive;
		rives.onRemove -= OnRemoveRive;

		riveInstances = null;

		Stop( sprites );
		sprites.onAdd -= OnAddSprite;

		Stop( canvases );
		canvases.onAdd -= OnAddCanvas;
		canvases.onRemove -= OnRemoveCanvas;

		canvasFilters = null;

		this.world = null;
	}

	public void Update ( World world, FrameTime time )
	{
		foreach ( var entity in rives.entities )
		{
			if ( !Get< DrawRive >( entity ).layout || !riveInstances.TryGetValue( entity, out var rive ) )
				continue;

			var rect = Get< Rect >( entity );
			if ( rect.width > 0f && rect.height > 0f )
				rive.Resize( rect.width, rect.height );
		}
	}

	public void DrawSystem ( World world, SpriteBatch batch )
	{
		var sortedCanvases = canvases.GetListCopy();
		sortedCanvases.Sort( ( a, b ) => GetSortOrder( a ).CompareTo( GetSortOrder( b ) ) );

		foreach ( uint canvas in sortedCanvases )
		{
			var sortedEntities = canvasFilters[ canvas ].GetListCopy();
			sortedEntities.Sort( ( a, b ) => GetSortOrder( a ).CompareTo( GetSortOrder( b ) ) );

			foreach ( var drawEntity in sortedEntities )
			{
				var color = GetColor( drawEntity );
				var rect = Get< Rect >( drawEntity );

				if ( TryGet< DrawSprite >( drawEntity, out DrawSprite sprite ) )
				{
					var position = Get< Position >( drawEntity );
					var texture = Toolbox.I.load.Texture( sprite.path );
					var pivot = new Vector2( rect.width * rect.pivot.x, rect.height * ( 1f - rect.pivot.y ) );
					batch.Draw( texture, position, null, color, 0f, pivot, Vector2.One );
				}

				if ( Has< DrawSolid >( drawEntity ) )
				{
					var position = Get< Position >( drawEntity );
					var bottomLeft = position.value - new Vector2( rect.width * rect.pivot.x, rect.height * rect.pivot.y );
					batch.FillRectangle( new RectangleF( bottomLeft.x, bottomLeft.y, rect.width, rect.height ), color );
				}

				if ( riveInstances.TryGetValue( drawEntity, out var rive ) )
				{
					var position = Get< Position >( drawEntity );
					var pivot = new Vector2( rive.width * rect.pivot.x, rive.height * ( 1f - rect.pivot.y ) );
					var scale = new Vector2( rect.width / rive.width, rect.height / rive.height );
					batch.Draw( rive, position, color, 0f, pivot, scale );
				}

				if ( TryGet< DrawText >( drawEntity, out DrawText drawText ) )
				{
					var position = Get< Position >( drawEntity );
					var font = Toolbox.I.load.Font( drawText.font, drawText.fontSize );
					var size = font.MeasureString( drawText.text );
					rect.width = size.x;
					rect.height = size.y;
					var topLeft = position.value + new Vector2( -rect.width * rect.pivot.x, rect.height * ( 1f - rect.pivot.y ) );
					batch.DrawString( font, drawText.text, topLeft, color );
				}
			}	
		}
	}

	public RiveInstance GetRive ( uint entity )
		=> riveInstances.TryGetValue( entity, out var rive ) ? rive : null;

	int GetSortOrder ( uint entity )
		=> TryGet( entity, out SortOrder sortOrder ) ? sortOrder : 0;

	Color GetColor ( uint entity )
		=> TryGet( entity, out DrawColor color ) ? color : Color.White;

	void OnAddCanvas ( uint entity )
	{
		canvasFilters[ entity ] = new FilterBuilder()
			.Related< OnCanvas >( entity )		
			.Include< Position >()
			.Include< Rect >()
			.Exclude< Hidden >()
			.ToFilter();

		Start( canvasFilters[ entity ] );
	}

	void OnRemoveCanvas ( uint entity )
	{
		Stop( canvasFilters[ entity ] );
		canvasFilters.Remove( entity );
	}

	void OnAddSprite ( uint entity )
	{
		var rect = Get< Rect >( entity );
		if ( rect.width != 0f && rect.height != 0f )
			return;

		var texture = Toolbox.I.load.Texture( Get< DrawSprite >( entity ).path );
		if ( rect.width == 0f )
			rect.width = texture.width;

		if ( rect.height == 0f )
			rect.height = texture.height;
	}

	void OnAddRive ( uint entity )
	{
		var drawRive = Get< DrawRive >( entity );
		var rect = Get< Rect >( entity );
		var riveFile = Toolbox.I.load.Rive( drawRive.path );

		var artboardSize = riveFile.ArtboardSize( drawRive.artboard );
		if ( rect.width == 0f )
			rect.width = artboardSize.x;

		if ( rect.height == 0f )
			rect.height = artboardSize.y;

		float fit = drawRive.layout ? 1f : MathF.Max( rect.width / artboardSize.x, rect.height / artboardSize.y );

		float resolution = fit * Toolbox.I.game.graphics.pixelScale;
		var rive = riveFile.CreateInstance( drawRive.artboard, drawRive.stateMachine, resolution );
		if ( drawRive.layout )
			rive.Resize( rect.width, rect.height );

		riveInstances[ entity ] = rive;
	}

	void OnRemoveRive ( uint entity )
	{
		riveInstances[ entity ].Dispose();
		riveInstances.Remove( entity );
	}
}