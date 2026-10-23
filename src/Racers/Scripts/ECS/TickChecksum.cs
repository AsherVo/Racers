namespace MintECS;

/// <summary><see cref="World.Checksum"/> after <see cref="World.tick"/> <see cref="tick"/> finished.</summary>
public struct TickChecksum
{
	public long tick;
	public ulong checksum;

	public TickChecksum ( long tick, ulong checksum )
	{
		this.tick = tick;
		this.checksum = checksum;
	}

	public override string ToString ()
		=> $"tick { tick }: { checksum:X16}";
}
