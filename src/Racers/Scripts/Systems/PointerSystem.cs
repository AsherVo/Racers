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

    const float MIN_ALPHA = 0.1f;

    RenderSystem renderSystem;
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
        renderSystem ??= Get< RenderSystem >();

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
        var origin = Get< Position >( targetEntity ).value;
        var rect = Get< Rect >( targetEntity );

        if ( Has< DrawSolid >( targetEntity ) && IsInSolid( position, origin, rect ) )
            return true;

        if ( TryGet( targetEntity, out DrawSprite sprite ) && IsInSprite( position, origin, rect, sprite ) )
            return true;

        if ( Has< DrawRive >( targetEntity ) && IsInRive( position, origin, rect, renderSystem.GetRive( targetEntity ) ) )
            return true;

        return false;
    }

    bool IsInSolid ( Vector2 position, Vector2 origin, Rect rect )
    {
        var bottomLeft = origin - new Vector2( rect.width * rect.pivot.x, rect.height * rect.pivot.y );

        return
            position.x >= bottomLeft.x && position.x <= bottomLeft.x + rect.width &&
            position.y >= bottomLeft.y && position.y <= bottomLeft.y + rect.height;
    }

    bool IsInSprite ( Vector2 position, Vector2 origin, Rect rect, DrawSprite sprite )
    {
        var texture = Toolbox.I.load.Texture( sprite.path );
        var pivot = new Vector2( rect.width * rect.pivot.x, rect.height * ( 1f - rect.pivot.y ) );
        var x = position.x - origin.x + pivot.x;
        var y = pivot.y - ( position.y - origin.y );

        return texture.AlphaAt( ( int )MathF.Floor( x ), ( int )MathF.Floor( y ) ) >= MIN_ALPHA;
    }

    bool IsInRive ( Vector2 position, Vector2 origin, Rect rect, RiveInstance rive )
    {
        if ( rive == null )
            return false;

        var pivot = new Vector2( rive.width * rect.pivot.x, rive.height * ( 1f - rect.pivot.y ) );
        var artboardPosition = new Vector2(
            ( position.x - origin.x ) * rive.width / rect.width + pivot.x,
            pivot.y - ( position.y - origin.y ) * rive.height / rect.height
        );

        return rive.AlphaAt( artboardPosition ) >= MIN_ALPHA;
    }
}
