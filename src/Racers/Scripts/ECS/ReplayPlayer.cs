using Engine;

namespace MintECS;

/// <summary>The first recorded checksum a replay didn't reproduce.</summary>
public readonly record struct ReplayDesync ( long tick, ulong expected, ulong actual )
{
	public override string ToString ()
		=> $"tick { tick }: expected { expected:X16}, got { actual:X16}";
}

/// <summary>
/// Drives a world from a recording. Before each tick it queues the commands recorded for that tick, in recorded order,
/// and drops any the view queued, since live input would change the outcome. After each tick with a recorded checksum
/// it compares the world's. It lives outside the world like <see cref="ReplayRecorder"/>; whoever owns the world stops
/// ticking once <see cref="isFinished"/> or <see cref="desync"/> is set.
/// </summary>
public sealed class ReplayPlayer
{
	readonly List< Command > recordedCommands;
	readonly List< TickChecksum > recordedChecksums;
	readonly Queue< Command > awaitingApply = new();
	int nextCommand;
	int nextChecksum;
	bool loggedDroppedCommands;
	World? world;

	/// <param name="commands">Stamped with their ticks, in the order they were applied.</param>
	/// <param name="checksums">In increasing tick order.</param>
	/// <param name="endTick">The last tick the recording covers. Playback stops there.</param>
	/// <exception cref="ArgumentException">Commands or checksums are out of order, or past <paramref name="endTick"/>.</exception>
	public ReplayPlayer ( IEnumerable< Command > commands, IEnumerable< TickChecksum > checksums, long endTick )
	{
		recordedCommands = commands.ToList();
		recordedChecksums = checksums.ToList();
		this.endTick = endTick;

		for ( int i = 0; i < recordedCommands.Count; i++ )
		{
			if ( recordedCommands[ i ].tick < 1 || recordedCommands[ i ].tick > endTick )
				throw new ArgumentException( $"Command { i } is stamped { recordedCommands[ i ].tick }, outside ticks 1 to { endTick }.", nameof( commands ) );

			if ( i > 0 && recordedCommands[ i ].tick < recordedCommands[ i - 1 ].tick )
				throw new ArgumentException( $"Command { i } (tick { recordedCommands[ i ].tick }) comes after a command at tick { recordedCommands[ i - 1 ].tick }.", nameof( commands ) );
		}

		for ( int i = 0; i < recordedChecksums.Count; i++ )
		{
			if ( recordedChecksums[ i ].tick > endTick )
				throw new ArgumentException( $"Checksum { i } is for tick { recordedChecksums[ i ].tick }, past the end tick { endTick }.", nameof( checksums ) );

			if ( i > 0 && recordedChecksums[ i ].tick <= recordedChecksums[ i - 1 ].tick )
				throw new ArgumentException( $"Checksum { i } (tick { recordedChecksums[ i ].tick }) doesn't come after tick { recordedChecksums[ i - 1 ].tick }.", nameof( checksums ) );
		}
	}

	public long endTick { get; }

	/// <summary>The world has reached <see cref="endTick"/>.</summary>
	public bool isFinished => world != null && world.tick >= endTick;

	/// <summary>The first checksum that didn't match, or null while everything has.</summary>
	public ReplayDesync? desync { get; private set; }

	/// <summary>How many checksums have matched so far.</summary>
	public int matchedChecksums { get; private set; }

	/// <summary>How many commands from the view were dropped because the replay was driving.</summary>
	public int droppedCommands { get; private set; }

	/// <summary>Fires once, on the first checksum that doesn't match, right after the tick that produced it.</summary>
	public event Action< ReplayDesync >? onDesync;

	/// <summary>
	/// Starts driving <paramref name="world"/> from its current tick. Commands recorded at or before it count as
	/// already applied, and a checksum recorded for it is compared straight away.
	/// </summary>
	/// <exception cref="InvalidOperationException">The world is already past <see cref="endTick"/>.</exception>
	public void Attach ( World world )
	{
		if ( world.tick > endTick )
			throw new InvalidOperationException( $"The world is at tick { world.tick }, past the replay's end at { endTick }." );

		Detach();
		this.world = world;
		world.onTickStart += QueueCommands;
		world.onCommandApplied += CheckApplied;
		world.onTickEnd += CompareChecksum;

		nextCommand = 0;
		while ( nextCommand < recordedCommands.Count && recordedCommands[ nextCommand ].tick <= world.tick )
			nextCommand++;

		nextChecksum = 0;
		while ( nextChecksum < recordedChecksums.Count && recordedChecksums[ nextChecksum ].tick < world.tick )
			nextChecksum++;

		CompareChecksum();
	}

	public void Detach ()
	{
		if ( world != null )
		{
			world.onTickStart -= QueueCommands;
			world.onCommandApplied -= CheckApplied;
			world.onTickEnd -= CompareChecksum;
		}

		awaitingApply.Clear();
		world = null;
	}

	void QueueCommands ()
	{
		var world = this.world!;
		if ( world.pendingCommands.Count > 0 )
		{
			droppedCommands += world.pendingCommands.Count;
			if ( !loggedDroppedCommands )
				Log.Info( "Replay: dropping commands from the view while the replay drives the sim." );

			loggedDroppedCommands = true;
			world.pendingCommands.Clear();
		}

		long applyTick = world.tick + 1;
		if ( nextCommand < recordedCommands.Count && recordedCommands[ nextCommand ].tick < applyTick )
			throw new InvalidOperationException( $"The replay's command for tick { recordedCommands[ nextCommand ].tick } was never applied: the world ticked while the player was detached." );

		awaitingApply.Clear();
		while ( nextCommand < recordedCommands.Count && recordedCommands[ nextCommand ].tick == applyTick )
		{
			var recorded = recordedCommands[ nextCommand++ ];
			awaitingApply.Enqueue( recorded );
			world.Send( recorded.Copy() );
		}
	}

	void CheckApplied ( Command command )
	{
		if ( !awaitingApply.TryDequeue( out var recorded ) )
			throw new InvalidOperationException( $"The world applied { command.GetType().Name } at tick { command.tick }, which the replay didn't queue." );

		if ( command.tick != recorded.tick || command.GetType() != recorded.GetType() )
			throw new InvalidOperationException( $"The world applied { command.GetType().Name } at tick { command.tick }, but the replay recorded { recorded.GetType().Name } at tick { recorded.tick }." );
	}

	void CompareChecksum ()
	{
		var world = this.world!;
		if ( awaitingApply.Count > 0 )
			throw new InvalidOperationException( $"Tick { world.tick } ended without applying { awaitingApply.Count } of the replay's commands." );

		if ( nextChecksum >= recordedChecksums.Count || recordedChecksums[ nextChecksum ].tick != world.tick )
			return;

		var expected = recordedChecksums[ nextChecksum++ ];
		ulong actual = world.Checksum();
		if ( actual == expected.checksum )
		{
			matchedChecksums++;
			return;
		}

		if ( desync != null )
			return;

		desync = new ReplayDesync( world.tick, expected.checksum, actual );
		onDesync?.Invoke( desync.Value );
	}
}
