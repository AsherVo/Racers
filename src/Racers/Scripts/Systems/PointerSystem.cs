using Engine;
using MintECS;

namespace Racers;

public class PointerSystem : WorldManipulator, ISystem
{
    Filter hoverFilter = new FilterBuilder()
        .Include< Interactable >()
        .Include< Position >()
        .Include< Rect >()
        .Include< HoverTarget >()
        .ToFilter();

    Filter clickFilter = new FilterBuilder()
        .Include< Interactable >()
        .Include< Position >()
        .Include< Rect >()
        .Include< ClickTarget >()
        .ToFilter();

    HashSet< uint > activeHovers = new();
    Vector2? hoverPosition;

    public void StartSystem ( World world )
    {
        this.world = world;

        Start( hoverFilter );
        Start( clickFilter );
    }

    public void StopSystem ( World world )
    {
        Stop( hoverFilter );
        Stop( clickFilter );

        this.world = null;
    }

    public void TickSystem ( World world )
    {
        var hoverEvents = Read< PointerHover >();
        if ( hoverEvents.Count > 0 )
            hoverPosition = hoverEvents[ ^1 ].position;

        var hoversThisFrame = new HashSet< uint >();
        if ( hoverPosition.HasValue )
        {
            foreach ( var entity in hoverFilter.entities )
            {
                if ( !IsInTarget( hoverPosition.Value, entity ) )
                    continue;

                hoversThisFrame.Add( entity );
                if ( activeHovers.Add( entity ) )
                {
                    Get< HoverTarget >( entity ).hovering = true;
                    Send( new StartHover { target = entity } );
                    Log.Info( "Start hover: " + GetName( entity ) );
                }
            }
        }

        if ( activeHovers.Count > 0 )
        {
            var lostHovers = activeHovers.Except( hoversThisFrame ).ToList();
            foreach ( var lostHover in lostHovers )
            {
                activeHovers.Remove( lostHover );

                if ( Exists( lostHover ) && Has< HoverTarget >( lostHover ) )
                    Get< HoverTarget >( lostHover ).hovering = false;

                Send( new StopHover { target = lostHover } );
                Log.Info( "Stop hover: " + GetName( lostHover ) );
            }
        }

        var clickEvents = Read< PointerClick >();
        foreach ( var clickEvent in clickEvents )
        {
            foreach ( var entity in clickFilter.entities )
            {
                if ( IsInTarget( clickEvent.position, entity ) )
                    Send( new Click { target = entity } );
            }
        }
    }

    public bool IsInTarget ( Vector2 position, uint targetEntity )
    {
        var rect = Get< Rect >( targetEntity );
        var bottomLeft = Get< Position >( targetEntity ).value - new Vector2( rect.width * rect.pivot.x, rect.height * rect.pivot.y );

        return
            position.x >= bottomLeft.x && position.x <= bottomLeft.x + rect.width &&
            position.y >= bottomLeft.y && position.y <= bottomLeft.y + rect.height;
    }
}
