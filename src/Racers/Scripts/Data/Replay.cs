using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine;
using MintECS;

namespace Racers;

public class ReplayHeader
{
    public int version = Replay.VERSION;
    public int ticksPerSecond;
    public ulong seed;
    public long startTick;
    public long endTick;
    public RaceConfig race;
    public string platform;
    public string runtime;
    public string recordedAt;
}

public class Replay
{
    public const int VERSION = 1;

    public ReplayHeader header = new();
    public List< Command > commands = new();
    public List< TickChecksum > checksums = new();

    public static Replay FromRecording ( ReplayRecorder recorder, World world, RaceConfig race )
    {
        recorder.RecordChecksum();

        return new Replay
        {
            header = new ReplayHeader
            {
                ticksPerSecond = world.ticksPerSecond,
                seed = 0,
                startTick = recorder.startTick,
                endTick = world.tick,
                race = race,
                platform = RuntimeInformation.RuntimeIdentifier,
                runtime = RuntimeFeature.IsDynamicCodeSupported ? RuntimeInformation.FrameworkDescription : $"{ RuntimeInformation.FrameworkDescription } (AOT)",
                recordedAt = DateTime.UtcNow.ToString( "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture ),
            },
            commands = recorder.commands.Select( command => command.Copy() ).ToList(),
            checksums = recorder.checksums.ToList(),
        };
    }

    public ReplayPlayer CreatePlayer ()
        => new ReplayPlayer( commands, checksums, header.endTick );

    public string ToYaml ()
        => Yaml.Serialize( this );

    public static Replay FromYaml ( string yaml, string source = null )
    {
        var replay = Yaml.Deserialize< Replay >( yaml, source: source );
        if ( replay.header.version != VERSION )
            throw new InvalidDataException( $"{ source ?? "Replay" } is version { replay.header.version }; this build reads version { VERSION }." );

        return replay;
    }

    public void Write ( string path )
    {
        Directory.CreateDirectory( Path.GetDirectoryName( Path.GetFullPath( path ) )! );
        File.WriteAllText( path, ToYaml() );
    }

    public static Replay Read ( string path )
        => FromYaml( File.ReadAllText( path ), path );
}
