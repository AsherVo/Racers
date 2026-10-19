namespace MintECS;

/// <summary>
/// A system with only these two methods is a utility: it has helpers for other code but never runs on its own.
/// Systems are never serialized. <see cref="StartSystem"/> rebuilds every field from the world, which may be fresh,
/// loaded mid-match, or restarted any number of times, and <see cref="StopSystem"/> releases them.
/// </summary>
public interface ISystem
{
	public void StartSystem ( World world );
	public void StopSystem ( World world );
}
