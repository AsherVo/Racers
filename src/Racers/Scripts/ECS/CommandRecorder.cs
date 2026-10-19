namespace MintECS;

/// <summary>
/// Records every command as a world applies it, stamped with its tick. Together with the content and seed, this is
/// everything needed to replay a match. It lives outside the world, so it keeps recording across system restarts; after
/// a load, attach it to the new world.
/// </summary>
public sealed class CommandRecorder
{
	readonly List< Command > recorded = new();
	World? world;

	/// <summary>In the order they were applied, so ticks never decrease.</summary>
	public IReadOnlyList< Command > commands => recorded;

	public void Attach ( World world )
	{
		Detach();
		this.world = world;
		world.onCommandApplied += Record;
	}

	public void Detach ()
	{
		if ( world != null )
			world.onCommandApplied -= Record;

		world = null;
	}

	public void Clear ()
		=> recorded.Clear();

	void Record ( Command command )
		=> recorded.Add( command );
}
