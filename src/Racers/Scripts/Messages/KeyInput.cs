using Engine;
using MintECS;

namespace Racers;

public class KeyInput : Message
{
	public Key key;
	public bool down;
}
