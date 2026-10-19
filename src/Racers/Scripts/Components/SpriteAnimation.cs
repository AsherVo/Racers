using Engine;
using MintECS;

namespace Racers;

public class SpriteAnimation : Component
{
	public string animation;
	public string playingAnimation;
	
	public float fps;

	public bool isPlaying = true;
	public bool loop = true;

	public int onFrame;
    public float timeLeftInFrame;		
}