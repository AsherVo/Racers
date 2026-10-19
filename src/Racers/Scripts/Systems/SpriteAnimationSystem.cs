using Engine;
using MintECS;

namespace Racers;

public class SpriteAnimationSystem : WorldManipulator, ISystem
{
    Filter filter;

    public void StartSystem ( World world )
    {
        this.world = world;

        filter = new FilterBuilder()
            .Include< SpriteAnimation >()        
            .Include< DrawSprite >()
            .ToFilter();

        Start( filter );
    }

    public void StopSystem ( World world )
    {
        Stop( filter );

        this.world = null;
    }

    public void TickSystem ( World world )
    {
        var timePassed = ReadSingle< TimePassed >();
        if ( timePassed == null )
            return;

        float dt = timePassed.dt;

        foreach ( var entity in filter.entities )
        {
            var sprite = Get< DrawSprite >( entity );
            var anim = Get< SpriteAnimation >( entity );
            if ( string.IsNullOrEmpty( anim.animation ) )
                continue;

            // Start animation
            if ( anim.playingAnimation != anim.animation )
            {
                anim.playingAnimation = anim.animation;
                anim.onFrame = -1;
                anim.timeLeftInFrame = 0f;
            }

            if ( anim.isPlaying )
                anim.timeLeftInFrame -= dt;

            // Update frame
            if ( anim.timeLeftInFrame <= 0f )
            {
                var sprites = Toolbox.I.load.GetSpritePaths( anim.animation );

                anim.onFrame++;
                if ( anim.onFrame >= sprites.Length )
                {
                    if ( anim.loop )
                    {
                        anim.onFrame = 0;
                    }
                    else
                    {
                        anim.onFrame = sprites.Length - 1;
                        anim.isPlaying = false;
                    }
                }

                sprite.path = sprites[ anim.onFrame ];
                anim.timeLeftInFrame = 1f / anim.fps;
            }   
        }
    }
}
