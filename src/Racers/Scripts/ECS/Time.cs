
namespace MintECS;

public enum Time
{
	None,
	Realtime, 	// Time passed on earth (un-pausable)
	App, 		// Time passed in the app (pauses on lost focus)
	Game		// Time passed in the game (pausable mid-game)
}