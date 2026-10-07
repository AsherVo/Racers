namespace Engine;

/// <param name="Path">Where in the document the error is, e.g. <c>rootNode.children[2].speed</c>. Empty at the root.</param>
/// <param name="Source">The file or other origin the text came from, if known.</param>
public sealed record YamlError( string Message, int Line, int Column, string Path = "", string? Source = null )
{
    public override string ToString ()
    {
        string location = Line > 0 ? $"{ Source ?? "<yaml>" }:{ Line }:{ Column }" : Source ?? "<yaml>";
        return Path.Length > 0 ? $"{ location }: { Path }: { Message }" : $"{ location }: { Message }";
    }
}

public sealed class YamlException : Exception
{
    public YamlException ( YamlError error )
        : this( [ error ] )
    {
    }

    public YamlException ( IReadOnlyList< YamlError > errors )
        : base( FormatMessage( errors ) )
    {
        Errors = errors;
    }

    public IReadOnlyList< YamlError > Errors { get; }

    static string FormatMessage ( IReadOnlyList< YamlError > errors ) => errors.Count switch
    {
        0 => "Invalid YAML.",
        1 => errors[ 0 ].ToString(),
        _ => $"{ errors.Count } YAML errors:{ Environment.NewLine }{ string.Join( Environment.NewLine, errors ) }",
    };
}
