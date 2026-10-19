using MintECS;

namespace Racers;

public abstract class UIEvent : Message
{
	public uint target;
}

public class StartHover : UIEvent {}

public class StopHover : UIEvent {}

public class Click : UIEvent {}