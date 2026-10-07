namespace Cli;

interface ICommand
{
    /// <summary>The words that invoke the command, e.g. "yaml validate".</summary>
    string Name { get; }

    string Summary { get; }

    string Usage { get; }

    /// <returns>The process exit code.</returns>
    int Run ( string[] args );
}

static class ExitCode
{
    public const int SUCCESS = 0;
    public const int FAILURE = 1;
    public const int USAGE = 2;
}

static class CommandRunner
{
    public static int Run ( IReadOnlyList< ICommand > commands, string[] args )
    {
        if ( args.Length == 0 || args[ 0 ] is "help" or "--help" or "-h" )
        {
            PrintHelp( commands );
            return args.Length == 0 ? ExitCode.USAGE : ExitCode.SUCCESS;
        }

        var command = commands
            .Where( candidate => Matches( candidate, args ) )
            .MaxBy( candidate => candidate.Name.Length );

        if ( command == null )
        {
            Console.Error.WriteLine( $"Unknown command '{ string.Join( ' ', args.Take( 2 ) ) }'." );
            PrintHelp( commands );
            return ExitCode.USAGE;
        }

        var rest = args.Skip( command.Name.Split( ' ' ).Length ).ToArray();
        if ( rest.Any( arg => arg is "--help" or "-h" ) )
        {
            Console.WriteLine( $"{ command.Summary }\n\nUsage: cli { command.Usage }" );
            return ExitCode.SUCCESS;
        }

        try
        {
            return command.Run( rest );
        }
        catch ( UsageException exception )
        {
            Console.Error.WriteLine( $"error: { exception.Message }" );
            Console.Error.WriteLine( $"Usage: cli { command.Usage }" );
            return ExitCode.USAGE;
        }
    }

    static bool Matches ( ICommand command, string[] args )
    {
        string[] words = command.Name.Split( ' ' );
        return args.Length >= words.Length && words.Select( ( word, i ) => word == args[ i ] ).All( match => match );
    }

    static void PrintHelp ( IReadOnlyList< ICommand > commands )
    {
        Console.WriteLine( "Usage: cli <command> [arguments]   (cli <command> --help for details)\n\nCommands:" );
        int width = commands.Max( command => command.Name.Length );
        foreach ( var command in commands )
            Console.WriteLine( $"  { command.Name.PadRight( width ) }  { command.Summary }" );
    }
}

sealed class UsageException ( string message ) : Exception( message );

/// <summary>Positional arguments plus <c>--name value</c> options and <c>--flag</c> switches.</summary>
sealed class Arguments
{
    readonly List< string > positional = new();
    readonly List< (string Name, string? Value) > options = new();

    /// <param name="valueOptions">Options that take a value, as <c>--name value</c> or <c>--name=value</c>.</param>
    /// <param name="flags">Options that take no value.</param>
    public Arguments ( string[] args, string[] valueOptions, string[] flags )
    {
        for ( int i = 0; i < args.Length; i++ )
        {
            string arg = args[ i ];
            if ( !arg.StartsWith( "--", StringComparison.Ordinal ) )
            {
                positional.Add( arg );
                continue;
            }

            int equals = arg.IndexOf( '=' );
            string name = equals > 0 ? arg[ 2..equals ] : arg[ 2.. ];
            if ( flags.Contains( name ) && equals < 0 )
            {
                options.Add( (name, null) );
            }
            else if ( valueOptions.Contains( name ) )
            {
                if ( equals > 0 )
                    options.Add( (name, arg[ ( equals + 1 ).. ]) );
                else if ( i + 1 < args.Length )
                    options.Add( (name, args[ ++i ]) );
                else
                    throw new UsageException( $"--{ name } needs a value." );
            }
            else
            {
                throw new UsageException( $"Unknown option --{ name }." );
            }
        }
    }

    public IReadOnlyList< string > Positional => positional;

    public bool Flag ( string name ) => options.Any( option => option.Name == name );

    public string? Value ( string name ) => Values( name ).LastOrDefault();

    public IEnumerable< string > Values ( string name ) =>
        options.Where( option => option.Name == name && option.Value != null ).Select( option => option.Value! );
}
