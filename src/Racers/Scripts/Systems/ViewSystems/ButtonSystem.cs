using Engine;
using MintECS;

namespace Racers;

public class ButtonSystem : WorldManipulator, IViewSystem
{
    const string RIVE_IDLE_TRIGGER = "idle";
    const string RIVE_HOVER_TRIGGER = "highlight";
    const string RIVE_CLICK_TRIGGER = "click";
    const string RIVE_LABEL = "label";

    Filter labels = new FilterBuilder()
        .Include< ButtonLabel >()
        .Include< DrawRive >()
        .ToFilter();

    Dictionary< uint, ( RiveInstance rive, string text ) > shownLabels = new();

    public void StartSystem ( World world )
    {
        this.world = world;

        shownLabels.Clear();
        labels.onRemove += OnRemoveLabel;
        Start( labels );
    }

    public void StopSystem ( World world )
    {
        Stop( labels );
        labels.onRemove -= OnRemoveLabel;
        shownLabels.Clear();

        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        UpdateLabels();

        var startHovers = Read< StartHover >();
        foreach ( var startHover in startHovers )
        {
            var target = startHover.target;
            if ( Has< DrawRive >( target ) )
            {
                var rive = world.Get< RenderSystem >().GetRive( target );
                rive?.FireViewModelTrigger( RIVE_HOVER_TRIGGER );
            }
        }

        var stopHovers = Read< StopHover >();
        foreach ( var stopHover in stopHovers )
        {
            var target = stopHover.target;
            if ( Exists( target ) && Has< DrawRive >( target ) )
            {
                var rive = world.Get< RenderSystem >().GetRive( target );
                rive?.FireViewModelTrigger( RIVE_IDLE_TRIGGER );
            }
        }

        var clicks = Read< Click >();
        foreach ( var click in clicks )
        {
            var target = click.target;
            if ( Exists( target ) && Has< DrawRive >( target ) )
            {
                var rive = world.Get< RenderSystem >().GetRive( target );
                rive?.FireViewModelTrigger( RIVE_CLICK_TRIGGER );
            }
        }
    }

    void UpdateLabels ()
    {
        var renderSystem = world.Get< RenderSystem >();
        foreach ( var entity in labels.entities )
        {
            var rive = renderSystem.GetRive( entity );
            if ( rive == null )
                continue;

            var text = Get< ButtonLabel >( entity ).text ?? "";
            if ( shownLabels.TryGetValue( entity, out var shown ) && shown.rive == rive && shown.text == text )
                continue;

            rive.SetViewModelString( RIVE_LABEL, text );
            shownLabels[ entity ] = ( rive, text );
        }
    }

    void OnRemoveLabel ( uint entity )
        => shownLabels.Remove( entity );
}
