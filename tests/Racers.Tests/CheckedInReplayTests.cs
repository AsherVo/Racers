namespace Racers.Tests;

/// <summary>
/// Plays every replay in tests/Replays headless and checks every recorded checksum. A failure means the sim's
/// outcome changed. If that was intended, re-record the replay (see docs/worklists/replays.md).
/// </summary>
public class CheckedInReplayTests
{
    static string REPLAY_DIRECTORY => Path.Combine( AppContext.BaseDirectory, "Replays" );

    public static TheoryData< string > ReplayFiles ()
    {
        var files = new TheoryData< string >();
        foreach ( string path in Directory.EnumerateFiles( REPLAY_DIRECTORY, "*.yaml" ).Order( StringComparer.Ordinal ) )
            files.Add( Path.GetFileName( path ) );

        return files;
    }

    [Fact]
    public void ThereAreReplaysToPlay ()
        => Assert.NotEmpty( ReplayFiles() );

    [Theory]
    [MemberData( nameof( ReplayFiles ) )]
    public void ReplayMatchesEveryChecksum ( string file )
    {
        HeadlessRunner.Setup( Match.CONTENT_ROOT );
        var replay = Replay.Read( Path.Combine( REPLAY_DIRECTORY, file ) );

        var result = HeadlessRunner.Play( replay );

        Assert.Null( result.desync );
        Assert.Equal( replay.header.endTick, result.tick );
        Assert.Equal( replay.checksums.Count, result.matchedChecksums );
        Assert.True( result.passed );
    }
}
