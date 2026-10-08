namespace Engine;

/// <param name="path">Where in the document the error is, e.g. <c>rootNode.children[2].speed</c>. Empty at the root.</param>
/// <param name="source">The file or other origin the text came from, if known.</param>
public sealed record YamlError ( string message, int line, int column, string path = "", string? source = null )
{
    public override string ToString ()
    {
        string location = line > 0 ? $"{source ?? "<yaml>"}:{line}:{column}" : source ?? "<yaml>";
        return path.Length > 0 ? $"{location}: {path}: {message}" : $"{location}: {message}";
    }
}

public sealed class YamlException : Exception
{
    public YamlException ( YamlError error )
        : this( [error] )
    {
    }

    public YamlException ( IReadOnlyList< YamlError > errors )
        : base( FormatMessage( errors ) )
    {
        this.errors = errors;
    }

    public IReadOnlyList< YamlError > errors { get; }

    static string FormatMessage ( IReadOnlyList< YamlError > errors ) => errors.Count switch
    {
        0 => "Invalid YAML.",
        1 => errors[0].ToString(),
        _ => $"{errors.Count} YAML errors:{Environment.NewLine}{string.Join( Environment.NewLine, errors )}",
    };
}
