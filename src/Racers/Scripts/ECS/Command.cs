using Engine;

namespace MintECS;

/// <summary>
/// The only way input reaches the simulation. The view sends a command, and the world applies it at the start of the
/// next fixed tick, stamped with that tick, where sim systems read it like any other message.
/// </summary>
[YamlPolymorphic]
public abstract class Command : Message
{
	public long tick;

	/// <summary>A deep copy, made by writing the command to YAML and reading it back, so it's exactly what a replay file holds.</summary>
	public Command Copy ()
		=> Yaml.Deserialize< Command >( Yaml.ToNode< Command >( this ) );
}
