using System.Reflection;
using Engine;

namespace Cli;

/// <summary>Checks that YAML files parse, and optionally that they read into a C# type without errors.</summary>
sealed class YamlValidateCommand : ICommand
{
    public string name => "yaml validate";

    public string summary => "Check that YAML files parse, and optionally that they read into a C# type.";

    public string usage =>
        "yaml validate <file-or-directory>... [--type <TypeName>] [--assembly <path.dll>]... [--allow-unknown-keys] [--quiet]\n\n"
        + "  Directories are searched recursively for *.yaml and *.yml files.\n"
        + "  --type                The class each file must read into, by full or short name (e.g. Racers.CarData).\n"
        + "                        Engine and game types are always available.\n"
        + "  --assembly            Also search this assembly for --type. Repeatable.\n"
        + "  --allow-unknown-keys  Don't report keys that match no member.\n"
        + "  --quiet               Only print problems.";

    public int Run ( string[] args )
    {
        var arguments = new Arguments( args, valueOptions: ["type", "assembly"], flags: ["allow-unknown-keys", "quiet"] );
        if ( arguments.positional.Count == 0 )
            throw new UsageException( "Give at least one file or directory." );

        var files = FindFiles( arguments.positional );
        var type = ResolveType( arguments.Value( "type" ), arguments.Values( "assembly" ) );
        var options = new YamlReadOptions { allowUnknownKeys = arguments.Flag( "allow-unknown-keys" ) };
        bool quiet = arguments.Flag( "quiet" );

        int failed = 0;
        foreach ( string file in files )
        {
            string relative = Path.GetRelativePath( Environment.CurrentDirectory, file );
            string display = relative.StartsWith( "..", StringComparison.Ordinal ) ? Path.GetFullPath( file ) : relative;
            IReadOnlyList< YamlError > errors;
            try
            {
                errors = Validate( File.ReadAllText( file ), display, type, options );
            }
            catch ( InvalidOperationException exception ) when ( type != null )
            {
                Console.Error.WriteLine( $"error: {type.FullName} can't be read from YAML: {exception.Message}" );
                return ExitCode.FAILURE;
            }

            if ( errors.Count == 0 )
            {
                if ( !quiet )
                    Console.WriteLine( $"ok    {display}" );

                continue;
            }

            failed++;
            foreach ( var error in errors )
                Console.WriteLine( $"error {error}" );
        }

        string against = type == null ? "" : $" against {type.FullName}";
        Console.WriteLine( $"{files.Count} file{( files.Count == 1 ? "" : "s" )} checked{against}: {failed} with errors." );
        return failed == 0 ? ExitCode.SUCCESS : ExitCode.FAILURE;
    }

    static IReadOnlyList< YamlError > Validate ( string text, string source, Type? type, YamlReadOptions options )
    {
        if ( type != null )
        {
            Yaml.TryDeserialize( text, type, out _, out var errors, options, source );
            return errors;
        }

        try
        {
            Yaml.ParseAll( text, source );
            return [];
        }
        catch ( YamlException exception )
        {
            return exception.errors;
        }
    }

    static List< string > FindFiles ( IEnumerable< string > paths )
    {
        var files = new List< string >();
        foreach ( string path in paths )
        {
            if ( Directory.Exists( path ) )
            {
                files.AddRange( Directory.EnumerateFiles( path, "*.*", SearchOption.AllDirectories )
                    .Where( file => Path.GetExtension( file ) is ".yaml" or ".yml" )
                    .Order( StringComparer.Ordinal ) );
            }
            else if ( File.Exists( path ) )
            {
                files.Add( path );
            }
            else
            {
                throw new UsageException( $"'{path}' does not exist." );
            }
        }

        return files;
    }

    static Type? ResolveType ( string? name, IEnumerable< string > assemblyPaths )
    {
        if ( name == null )
            return null;

        var assemblies = new List< Assembly > { typeof( Yaml ).Assembly, typeof( Racers.RacersGame ).Assembly };
        foreach ( string path in assemblyPaths )
        {
            if ( !File.Exists( path ) )
                throw new UsageException( $"Assembly '{path}' does not exist." );

            assemblies.Add( Assembly.LoadFrom( Path.GetFullPath( path ) ) );
        }

        var types = assemblies.Distinct().SelectMany( LoadableTypes ).Where( type => !type.ContainsGenericParameters ).ToList();
        var matches = types.Where( type => type.FullName == name ).ToList();
        if ( matches.Count == 0 )
            matches = types.Where( type => type.Name == name || type.FullName?.Replace( '+', '.' ) == name ).ToList();

        if ( matches.Count == 1 )
            return matches[0];

        if ( matches.Count > 1 )
            throw new UsageException( $"'{name}' is ambiguous: {string.Join( ", ", matches.Select( type => type.FullName ) )}." );

        var similar = types.Where( type => type.Name.Contains( name, StringComparison.OrdinalIgnoreCase ) ).Take( 5 ).Select( type => type.FullName );
        string suggestion = similar.Any() ? $" Did you mean: {string.Join( ", ", similar )}?" : "";
        throw new UsageException( $"No type named '{name}'.{suggestion}" );
    }

    static IEnumerable< Type > LoadableTypes ( Assembly assembly )
    {
        try
        {
            return assembly.GetTypes();
        }
        catch ( ReflectionTypeLoadException exception )
        {
            return exception.Types.OfType< Type >();
        }
    }
}
