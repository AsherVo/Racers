using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Engine;

/// <summary>
/// One row of the tree a development tool shows of the running game: a label, a value, and — only when the
/// tool has it expanded — its children. Built fresh for every request. The tool identifies a row by its
/// path, the <see cref="key"/>s from the root joined with <see cref="InspectorQuery.Join"/>, so a row stays
/// the same row (and stays expanded) for as long as its key does.
/// </summary>
public sealed class InspectorNode ( string key, string label )
{
    /// <summary>Longest summary shown for a collapsed map or list before it falls back to a count.</summary>
    const int MAX_SUMMARY_LENGTH = 80;

    const string TRIM_JUSTIFICATION = "Inspected objects are game and engine types, which are kept by rooting their assemblies.";

    /// <summary>Unique among its siblings.</summary>
    public string key { get; } = key;

    public string label { get; set; } = label;

    /// <summary>Shown muted beside the label, such as an entity's id.</summary>
    public string? note { get; set; }

    public string? value { get; set; }

    /// <summary>True when <see cref="value"/> describes the row's children rather than being data itself.</summary>
    public bool valueIsSummary { get; set; }

    /// <summary>Whether the row has children to show. They're only filled in when the row is expanded.</summary>
    public bool expandable { get; set; }

    public List< InspectorNode >? children { get; set; }

    /// <summary>
    /// A row for <paramref name="data"/> as its YAML would have it: a scalar is a value, and a map or list is
    /// expandable, summarized while collapsed and broken into child rows once <paramref name="path"/> is expanded.
    /// An object that can't be written as YAML is shown with the reason instead of failing the whole tree.
    /// </summary>
    [UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = TRIM_JUSTIFICATION )]
    public static InspectorNode Describe ( string key, string label, object? data, string path, InspectorQuery query )
    {
        YamlNode yaml;
        try
        {
            yaml = data == null ? YamlScalar.Null() : Yaml.ToNode( data, data.GetType() );
        }
        catch ( Exception exception ) when ( exception is InvalidOperationException or NotSupportedException or ArgumentException )
        {
            return new InspectorNode( key, label ) { value = $"<{exception.Message}>", valueIsSummary = true };
        }

        return FromYaml( key, label, yaml, path, query );
    }

    static InspectorNode FromYaml ( string key, string label, YamlNode yaml, string path, InspectorQuery query )
    {
        var node = new InspectorNode( key, label );
        switch ( yaml )
        {
            case YamlScalar scalar:
                node.value = scalar.value;
                return node;

            case YamlMap map:
                node.expandable = map.Count > 0;
                node.value = Summarize( yaml ) ?? $"{map.Count} field{( map.Count == 1 ? "" : "s" )}";
                node.valueIsSummary = true;
                if ( node.expandable && query.IsExpanded( path ) )
                {
                    node.children = new List< InspectorNode >( map.Count );
                    for ( int i = 0; i < map.Count; i++ )
                    {
                        var ( keyNode, valueNode ) = map[i];
                        string childKey = keyNode is YamlScalar keyScalar ? keyScalar.value : i.ToString();
                        node.children.Add( FromYaml( childKey, childKey, valueNode, InspectorQuery.Join( path, childKey ), query ) );
                    }
                }

                return node;

            case YamlList list:
                node.expandable = list.Count > 0;
                node.value = Summarize( yaml ) ?? $"{list.Count} item{( list.Count == 1 ? "" : "s" )}";
                node.valueIsSummary = true;
                if ( node.expandable && query.IsExpanded( path ) )
                {
                    node.children = new List< InspectorNode >( list.Count );
                    for ( int i = 0; i < list.Count; i++ )
                    {
                        string index = i.ToString();
                        node.children.Add( FromYaml( index, $"[{i}]", list[i], InspectorQuery.Join( path, index ), query ) );
                    }
                }

                return node;

            default:
                node.value = yaml.kindName;
                return node;
        }
    }

    /// <summary>A collapsed map or list in YAML's flow style — <c>{X: 10, Y: 2.5}</c> — or null when that would be too long to read at a glance.</summary>
    static string? Summarize ( YamlNode yaml )
    {
        var builder = new StringBuilder();
        return Append( yaml, builder ) && builder.Length <= MAX_SUMMARY_LENGTH ? builder.ToString() : null;
    }

    static bool Append ( YamlNode yaml, StringBuilder builder )
    {
        if ( builder.Length > MAX_SUMMARY_LENGTH )
            return false;

        switch ( yaml )
        {
            case YamlScalar scalar:
                builder.Append( scalar.value );
                return true;

            case YamlMap map:
                builder.Append( '{' );
                for ( int i = 0; i < map.Count; i++ )
                {
                    if ( i > 0 )
                        builder.Append( ", " );

                    var ( keyNode, valueNode ) = map[i];
                    if ( !Append( keyNode, builder ) )
                        return false;

                    builder.Append( ": " );
                    if ( !Append( valueNode, builder ) )
                        return false;
                }

                builder.Append( '}' );
                return true;

            case YamlList list:
                builder.Append( '[' );
                for ( int i = 0; i < list.Count; i++ )
                {
                    if ( i > 0 )
                        builder.Append( ", " );

                    if ( !Append( list[i], builder ) )
                        return false;
                }

                builder.Append( ']' );
                return true;

            default:
                return false;
        }
    }
}

/// <summary>
/// What the tool asking for the tree has expanded, by path. A source only describes the children of
/// expanded rows, which is what keeps a world with thousands of entities cheap to send.
/// </summary>
public sealed class InspectorQuery ( IEnumerable< string > expandedPaths )
{
    readonly HashSet< string > expanded = new( expandedPaths, StringComparer.Ordinal );

    public static readonly InspectorQuery Nothing = new( [] );

    public bool IsExpanded ( string path ) => expanded.Contains( path );

    /// <summary>The path of a child row. A <c>/</c> in a key is escaped, so any string can be a key.</summary>
    public static string Join ( string parentPath, string childKey )
    {
        string escaped = childKey.Replace( "%", "%25" ).Replace( "/", "%2F" );
        return parentPath.Length == 0 ? escaped : $"{parentPath}/{escaped}";
    }
}

/// <summary>Something the inspector can show, such as an ECS world. Register it with <see cref="Inspector.Register"/>.</summary>
public interface IInspectable
{
    /// <summary>
    /// The root row. Its path is its <see cref="InspectorNode.key"/>, which should be unique among sources and
    /// stable for the source's lifetime. Called on the game thread, between frames.
    /// </summary>
    InspectorNode Inspect ( InspectorQuery query );
}
