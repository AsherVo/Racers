using Engine;
using MintECS;
using Racers;

using System.Collections;
using System.Collections.Generic;

namespace Racers;

public class GameViewSystem : WorldManipulator, IViewSystem
{
    public UIStateMachine uiState { get; private set; }
    public int width { get; private set; }
    public int height { get; private set; }

    public uint raceCanvas { get; private set; }
    public uint uiCanvas { get; private set; }
    public uint debugCanvas { get; private set; }

    uint statsText;
    uint statsShadow;

    public void StartSystem ( World world )
    {
        this.world = world;

        uiState = new UIStateMachine();
        uiState.logDebug = true;
    }

    public void StopSystem ( World world )
    {
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        
    }

    public void Setup ( Graphics graphics, RaceConfig race )
    {
        this.width = graphics.width;
        this.height = graphics.height;

        SetupDebug();

        raceCanvas = Create( "Race Canvas",
            new Canvas {},
            new SortOrder { value = 0 }
        );

        uiCanvas = Create( "UI Canvas",
            new Canvas {}, 
            new SortOrder { value = 5000 }
        );

        // Race
        var prefab = Get< PrefabSystem >();
        var rive = prefab.CreateFromData( race.background, raceCanvas );
        GetOrAdd< SortOrder >( rive ).value = -100;

        Get< GridViewSystem >().Setup( raceCanvas );        

        // UI
        var topSpeed = prefab.CreateFromData( "UI_TopSpeed", uiCanvas );
        Get< Position >( topSpeed ).value = new Vector2( width, height );
        Get< Rect >( topSpeed ).pivot = Vector2.One;

        var speedometer = Get< RenderSystem >().GetRive( topSpeed );
        speedometer.SetViewModelString( "speedText", 5.ToString() );

        Get< MenuViewSystem >().Setup( uiCanvas, height, "Race", "Garage", "Options", "Quit" );

        // Wiring
        Get< UnitViewSystem >().Refresh();
    }

    void SetupDebug ()
    {
        debugCanvas = Create( "Debug Canvas",
            new Canvas {},
            new SortOrder { value = 9999 }
        ); 

        statsShadow = Get< PrefabSystem >().CreateText( "Stats Shadow", "", "Crates", 8, Color.Black.WithAlpha( 0.6f ), debugCanvas );
        Get< Position >( statsShadow ).value = new Vector2( 6, height - 5 );
        Get< Rect >( statsShadow ).pivot = new Vector2( 0, 1 );

        statsText = Get< PrefabSystem >().CreateText( "Stats", "", "Crates", 8, Color.White, debugCanvas );
        Get< Position >( statsText ).value = new Vector2( 5, height - 4 );
        Get< Rect >( statsText ).pivot = new Vector2( 0, 1 );
        Add( statsText, new SortOrder { value = 1 } );
    }

    public void DrawDebugStats ( string stats )
    {
        Get< DrawText >( statsText ).text = stats;
        Get< DrawText >( statsShadow ).text = stats;
    }
}