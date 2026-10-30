using Engine;
using MintECS;

namespace Racers;

public class PointerSystem : WorldManipulator, IViewSystem
{
    Filter targets = new FilterBuilder()
        .Include< Interactable >()
        .Include< Position >()
        .Include< Rect >()
        .Exclude< Hidden >()
        .ToFilter();

    const float MIN_ALPHA = 0.1f;

    RenderSystem renderSystem;
    List< uint > frontToBack = new();
    HashSet< uint > activeHovers = new();
    Vector2? hoverPosition;

    uint pressedTarget;
    uint riveOwner;
    bool riveCaptured;

    public void StartSystem ( World world )
    {
        this.world = world;

        Start( targets );

        activeHovers.Clear();
        hoverPosition = null;
        pressedTarget = 0;
        riveOwner = 0;
        riveCaptured = false;
        foreach ( var entity in targets.entities )
        {
            if ( TryGet( entity, out HoverTarget hover ) && hover.hovering )
                activeHovers.Add( entity );
        }
    }

    public void StopSystem ( World world )
    {
        Stop( targets );

        frontToBack.Clear();
        activeHovers.Clear();
        hoverPosition = null;
        pressedTarget = 0;
        riveOwner = 0;
        riveCaptured = false;
        renderSystem = null;
        this.world = null;
    }

    public void Update ( World world, FrameTime time )
    {
        renderSystem = Get< RenderSystem >();
        SortTargets();

        if ( riveOwner != 0 && GetRiveInput( riveOwner ) == null )
        {
            riveOwner = 0;
            riveCaptured = false;
        }

        var hoverEvents = Read< PointerHover >();
        if ( hoverEvents.Count > 0 )
        {
            hoverPosition = hoverEvents[ ^1 ].position;
            MoveRive( hoverPosition.Value );
        }

        foreach ( var down in Read< PointerDown >() )
        {
            PressRive( down.position );
            pressedTarget = riveCaptured ? 0 : GetTopTarget( down.position );
        }

        foreach ( var up in Read< PointerClick >() )
        {
            ReleaseRive( up.position );

            var top = GetTopTarget( up.position );
            if ( top != 0 && top == pressedTarget && Has< ClickTarget >( top ) )
                Send( new Click { target = top } );

            pressedTarget = 0;
        }

        UpdateHovers();
    }

    void UpdateHovers ()
    {
        var hovered = hoverPosition.HasValue && !riveCaptured ? GetTopTarget( hoverPosition.Value ) : 0;
        if ( hovered != 0 && !Has< HoverTarget >( hovered ) )
            hovered = 0;

        if ( hovered != 0 && activeHovers.Add( hovered ) )
        {
            Get< HoverTarget >( hovered ).hovering = true;
            Send( new StartHover { target = hovered } );
        }

        if ( activeHovers.Count == 0 || ( activeHovers.Count == 1 && hovered != 0 ) )
            return;

        var lostHovers = activeHovers.Where( entity => entity != hovered ).ToList();
        foreach ( var lostHover in lostHovers )
        {
            activeHovers.Remove( lostHover );

            if ( Exists( lostHover ) && Has< HoverTarget >( lostHover ) )
                Get< HoverTarget >( lostHover ).hovering = false;

            Send( new StopHover { target = lostHover } );
        }
    }

    void MoveRive ( Vector2 position )
    {
        if ( !riveCaptured )
            SetRiveOwner( GetRiveTarget( position ), position );

        var rive = GetRiveInput( riveOwner );
        rive?.PointerMove( ToArtboard( position, riveOwner, rive ) );
    }

    void PressRive ( Vector2 position )
    {
        if ( !riveCaptured )
            SetRiveOwner( GetRiveTarget( position ), position );

        var rive = GetRiveInput( riveOwner );
        if ( rive == null )
            return;

        riveCaptured = true;
        rive.PointerDown( ToArtboard( position, riveOwner, rive ) );
    }

    void ReleaseRive ( Vector2 position )
    {
        var rive = GetRiveInput( riveOwner );
        if ( rive != null && ( riveCaptured || riveOwner == GetRiveTarget( position ) ) )
            rive.PointerUp( ToArtboard( position, riveOwner, rive ) );

        riveCaptured = false;
        SetRiveOwner( GetRiveTarget( position ), position );
    }

    void SetRiveOwner ( uint owner, Vector2 position )
    {
        if ( owner == riveOwner )
            return;

        var previous = GetRiveInput( riveOwner );
        previous?.PointerExit( ToArtboard( position, riveOwner, previous ) );
        riveOwner = owner;
    }

    uint GetRiveTarget ( Vector2 position )
    {
        var top = GetTopTarget( position );
        return top != 0 && Has< RiveInput >( top ) ? top : 0;
    }

    RiveInstance GetRiveInput ( uint entity )
    {
        if ( entity == 0 || !Exists( entity ) || !Has< RiveInput >( entity ) )
            return null;

        return renderSystem.GetRive( entity );
    }

    public uint GetTopTarget ( Vector2 position )
    {
        foreach ( var entity in frontToBack )
        {
            if ( IsInTarget( position, entity ) )
                return entity;
        }

        return 0;
    }

    void SortTargets ()
    {
        frontToBack.Clear();
        foreach ( var entity in targets.entities )
        {
            var canvas = GetRelated< OnCanvas >( entity );
            if ( canvas != 0 && Exists( canvas ) && Has< Canvas >( canvas ) && !Has< Hidden >( canvas ) )
                frontToBack.Add( entity );
        }

        frontToBack.Sort( ( a, b ) =>
        {
            int byCanvas = GetSortOrder( GetRelated< OnCanvas >( b ) ).CompareTo( GetSortOrder( GetRelated< OnCanvas >( a ) ) );
            if ( byCanvas != 0 )
                return byCanvas;

            int byEntity = GetSortOrder( b ).CompareTo( GetSortOrder( a ) );
            return byEntity != 0 ? byEntity : b.CompareTo( a );
        });
    }

    int GetSortOrder ( uint entity )
        => TryGet( entity, out SortOrder sortOrder ) ? sortOrder : 0;

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

        return rive.AlphaAt( ToArtboard( position, origin, rect, rive ) ) >= MIN_ALPHA;
    }

    Vector2 ToArtboard ( Vector2 position, uint entity, RiveInstance rive )
        => ToArtboard( position, Get< Position >( entity ).value, Get< Rect >( entity ), rive );

    Vector2 ToArtboard ( Vector2 position, Vector2 origin, Rect rect, RiveInstance rive )
    {
        var pivot = new Vector2( rive.width * rect.pivot.x, rive.height * ( 1f - rect.pivot.y ) );
        return new Vector2(
            ( position.x - origin.x ) * rive.width / rect.width + pivot.x,
            pivot.y - ( position.y - origin.y ) * rive.height / rect.height
        );
    }
}
