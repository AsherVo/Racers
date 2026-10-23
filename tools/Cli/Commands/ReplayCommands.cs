using Engine;
using Racers;

namespace Cli;

/// <summary>Prints a replay's header and what it contains.</summary>
sealed class ReplayInfoCommand : ICommand
{
    public string name => "replay info";

    public string summary => "Print a replay's header and what it contains.";

    public string usage => "replay info <replay.yaml>...";

    public int Run ( string[] args )
    {
        var arguments = new Arguments( args, valueOptions: [], flags: [] );
        var files = ReplayFiles.Find( arguments.positional );

        int failed = 0;
        foreach ( string file in files )
        {
            if ( !ReplayFiles.TryRead( file, out var replay ) )
            {
                failed++;
                continue;
            }

            var header = replay.header;
            long ticks = header.endTick - header.startTick;
            Console.WriteLine( file );
            Console.WriteLine( $"  ticks       {header.startTick}-{header.endTick} ({ticks} ticks, {ticks / ( double )header.ticksPerSecond:0.##}s at {header.ticksPerSecond}/s)" );
            Console.WriteLine( $"  race        {header.race.width}x{header.race.height}, {header.race.units?.Length ?? 0} units, background {header.race.background ?? "none"}" );
            Console.WriteLine( $"  seed        {header.seed}" );
            Console.WriteLine( $"  recorded    {header.recordedAt} on {header.platform}, {header.runtime}" );
            Console.WriteLine( $"  commands    {replay.commands.Count}" );
            foreach ( var group in replay.commands.GroupBy( command => command.GetType().Name ).OrderBy( group => group.Key, StringComparer.Ordinal ) )
                Console.WriteLine( $"    {group.Key,-12}{group.Count()}" );

            Console.WriteLine( $"  checksums   {replay.checksums.Count}" );
        }

        return failed == 0 ? ExitCode.SUCCESS : ExitCode.FAILURE;
    }
}

/// <summary>Plays replays headless and checks every recorded checksum.</summary>
sealed class ReplayVerifyCommand : ICommand
{
    public string name => "replay verify";

    public string summary => "Play replays headless and check every recorded checksum.";

    public string usage =>
        "replay verify <file-or-directory>... [--content <dir>] [--dump <dir>] [--quiet]\n\n"
        + "  Directories are searched recursively for *.yaml files.\n"
        + "  --content  The game content to build matches from. Defaults to the copy built next to the CLI.\n"
        + "  --dump     On a desync, write the sim state at that tick here, for diffing.\n"
        + "  --quiet    Only print failures.\n\n"
        + "  Exits 1 if any replay fails, so it works with git bisect run.";

    public int Run ( string[] args )
    {
        var arguments = new Arguments( args, valueOptions: ["content", "dump"], flags: ["quiet"] );
        var files = ReplayFiles.Find( arguments.positional );
        string? dumpDirectory = arguments.Value( "dump" );
        bool quiet = arguments.Flag( "quiet" );
        HeadlessRunner.Setup( ReplayFiles.ContentRoot( arguments.Value( "content" ) ) );

        int failed = 0;
        foreach ( string file in files )
        {
            if ( !ReplayFiles.TryRead( file, out var replay ) )
            {
                failed++;
                continue;
            }

            ReplayResult result;
            try
            {
                result = HeadlessRunner.Play( replay );
            }
            catch ( Exception exception ) when ( exception is InvalidDataException or InvalidOperationException or KeyNotFoundException or IOException )
            {
                failed++;
                Console.WriteLine( $"FAIL  {file}: {exception.Message}" );
                continue;
            }

            string checksums = $"{result.matchedChecksums}/{result.recordedChecksums} checksums";
            if ( result.passed )
            {
                if ( !quiet )
                    Console.WriteLine( $"ok    {file} (tick {result.tick}, {checksums})" );

                continue;
            }

            failed++;
            string reason = result.desync is { } desync ? $"desync at {desync}" : $"stopped at tick {result.tick} of {result.endTick}";
            Console.WriteLine( $"FAIL  {file}: {reason} ({checksums})" );

            if ( dumpDirectory != null && result.desync != null )
            {
                Directory.CreateDirectory( dumpDirectory );
                string dumpPath = Path.Combine( dumpDirectory, $"{Path.GetFileNameWithoutExtension( file )}.desync-{result.tick}.yaml" );
                File.WriteAllText( dumpPath, result.world.SaveSimState() );
                Console.WriteLine( $"      sim state written to {dumpPath}" );
            }
        }

        Console.WriteLine( $"{files.Count} replay{( files.Count == 1 ? "" : "s" )} verified: {failed} failed." );
        return failed == 0 ? ExitCode.SUCCESS : ExitCode.FAILURE;
    }
}

/// <summary>Replays the commands of existing replays and rewrites their checksums, after an intended sim change.</summary>
sealed class ReplayRerecordCommand : ICommand
{
    public string name => "replay rerecord";

    public string summary => "Rewrite replays' checksums from their commands, after an intended sim change.";

    public string usage =>
        "replay rerecord <file-or-directory>... [--content <dir>]\n\n"
        + "  Plays each replay's commands headless without checking, then overwrites the file with fresh checksums.\n"
        + "  Only do this when the sim's outcome was meant to change; otherwise a failing replay is a bug.";

    public int Run ( string[] args )
    {
        var arguments = new Arguments( args, valueOptions: ["content"], flags: [] );
        var files = ReplayFiles.Find( arguments.positional );
        HeadlessRunner.Setup( ReplayFiles.ContentRoot( arguments.Value( "content" ) ) );

        int failed = 0;
        foreach ( string file in files )
        {
            if ( !ReplayFiles.TryRead( file, out var replay ) )
            {
                failed++;
                continue;
            }

            try
            {
                var rerecorded = HeadlessRunner.Rerecord( replay );
                rerecorded.Write( file );
                int changed = rerecorded.checksums.Count( checksum => !replay.checksums.Contains( checksum ) );
                Console.WriteLine( $"wrote {file} ({changed} of {rerecorded.checksums.Count} checksums changed)" );
            }
            catch ( Exception exception ) when ( exception is InvalidDataException or InvalidOperationException or KeyNotFoundException or IOException )
            {
                failed++;
                Console.WriteLine( $"FAIL  {file}: {exception.Message}" );
            }
        }

        return failed == 0 ? ExitCode.SUCCESS : ExitCode.FAILURE;
    }
}

static class ReplayFiles
{
    public static List< string > Find ( IReadOnlyList< string > paths )
    {
        if ( paths.Count == 0 )
            throw new UsageException( "Give at least one replay file or directory." );

        var files = new List< string >();
        foreach ( string path in paths )
        {
            if ( Directory.Exists( path ) )
                files.AddRange( Directory.EnumerateFiles( path, "*.yaml", SearchOption.AllDirectories ).Order( StringComparer.Ordinal ) );
            else if ( File.Exists( path ) )
                files.Add( path );
            else
                throw new UsageException( $"'{path}' does not exist." );
        }

        return files;
    }

    public static bool TryRead ( string file, out Replay replay )
    {
        try
        {
            replay = Replay.Read( file );
            return true;
        }
        catch ( Exception exception ) when ( exception is YamlException or InvalidDataException or IOException )
        {
            Console.WriteLine( $"FAIL  {file}: can't read it as a replay: {exception.Message}" );
            replay = null!;
            return false;
        }
    }

    public static string ContentRoot ( string? option )
    {
        string root = option ?? Path.Combine( AppContext.BaseDirectory, "Content" );
        if ( !Directory.Exists( root ) )
            throw new UsageException( $"Content directory '{root}' does not exist." );

        return root;
    }
}
