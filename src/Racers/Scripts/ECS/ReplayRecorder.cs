namespace MintECS;

/// <summary>
/// Records a match as it's played: a copy of every command as a world applies it, stamped with its tick, and a
/// <see cref="World.Checksum"/> every <see cref="checksumInterval"/> ticks. With the match setup and seed, the commands
/// are everything needed to replay the match, and the checksums prove a replay matched. It lives outside the world,
/// so it keeps recording across system restarts; after a load, attach it to the new world. It only watches: the sim
/// runs identically with and without it.
/// </summary>
public sealed class ReplayRecorder
{
	readonly List< Command > recordedCommands = new();
	readonly List< TickChecksum > recordedChecksums = new();
	World? world;

	/// <exception cref="ArgumentOutOfRangeException"><paramref name="checksumInterval"/> isn't positive.</exception>
	public ReplayRecorder ( int checksumInterval )
	{
		if ( checksumInterval <= 0 )
			throw new ArgumentOutOfRangeException( nameof( checksumInterval ), checksumInterval, "Must be positive." );

		this.checksumInterval = checksumInterval;
	}

	public int checksumInterval { get; }

	/// <summary>The tick recording started at: the world's tick when first attached, or when last cleared while attached.</summary>
	public long startTick { get; private set; } = -1;

	/// <summary>Copies, in the order they were applied, so ticks never decrease.</summary>
	public IReadOnlyList< Command > commands => recordedCommands;

	/// <summary>In tick order, starting with the state recording started from.</summary>
	public IReadOnlyList< TickChecksum > checksums => recordedChecksums;

	/// <summary>Starts recording <paramref name="world"/>, beginning with a checksum of its current state.</summary>
	public void Attach ( World world )
	{
		Detach();
		this.world = world;
		world.onCommandApplied += RecordCommand;
		world.onTickEnd += OnTickEnd;

		if ( startTick < 0 )
			startTick = world.tick;

		RecordChecksum();
	}

	public void Detach ()
	{
		if ( world != null )
		{
			world.onCommandApplied -= RecordCommand;
			world.onTickEnd -= OnTickEnd;
		}

		world = null;
	}

	/// <summary>Forgets everything recorded. If attached, recording restarts from the world's current tick.</summary>
	public void Clear ()
	{
		recordedCommands.Clear();
		recordedChecksums.Clear();
		startTick = world?.tick ?? -1;

		if ( world != null )
			RecordChecksum();
	}

	/// <summary>Records a checksum of the current tick, unless it already has one. Call before saving, so the recording ends on one.</summary>
	/// <exception cref="InvalidOperationException">Not attached to a world, or the world is behind the recording (<see cref="Clear"/> first).</exception>
	public void RecordChecksum ()
	{
		if ( world == null )
			throw new InvalidOperationException( "The recorder isn't attached to a world." );

		if ( recordedChecksums.Count > 0 && recordedChecksums[ ^1 ].tick > world.tick )
			throw new InvalidOperationException( $"The world is at tick { world.tick }, behind the recording's tick { recordedChecksums[ ^1 ].tick }. Clear the recorder before recording an earlier state." );

		if ( recordedChecksums.Count > 0 && recordedChecksums[ ^1 ].tick == world.tick )
			return;

		recordedChecksums.Add( new TickChecksum( world.tick, world.Checksum() ) );
	}

	void RecordCommand ( Command command )
		=> recordedCommands.Add( command.Copy() );

	void OnTickEnd ()
	{
		if ( world!.tick % checksumInterval == 0 )
			RecordChecksum();
	}
}
