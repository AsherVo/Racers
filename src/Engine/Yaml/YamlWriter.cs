using System.Text;

namespace Engine;

/// <summary>
/// Writes nodes as block-style YAML. Strings are written plain when they read back as the same string, as
/// literal blocks when they span lines, and double-quoted otherwise. Collections reached more than once
/// (and anchored scalars reached more than once) are written once and then referenced with aliases.
/// </summary>
sealed class YamlWriter
{
    const int INDENT = 2;

    readonly StringBuilder output = new();
    readonly Dictionary< YamlNode, string > anchorNames = new( ReferenceEqualityComparer.Instance );
    readonly HashSet< YamlNode > anchorsWritten = new( ReferenceEqualityComparer.Instance );

    enum Position
    {
        Root,
        MapValue,
        SequenceItem,
    }

    public static string Write ( YamlNode node )
    {
        var writer = new YamlWriter();
        writer.WriteDocument( node );
        return writer.output.ToString();
    }

    public static string WriteAll ( IEnumerable< YamlNode > documents )
    {
        var writer = new YamlWriter();
        foreach ( var document in documents )
        {
            writer.output.Append( "---\n" );
            writer.WriteDocument( document );
        }

        return writer.output.ToString();
    }

    void WriteDocument ( YamlNode root )
    {
        anchorNames.Clear();
        anchorsWritten.Clear();
        AssignAnchors( root );

        if ( IsEmptyPlain( root ) && root.Tag == null && !anchorNames.ContainsKey( root ) )
            return;

        WriteValue( root, 0, Position.Root );
    }

    void AssignAnchors ( YamlNode root )
    {
        var visits = new Dictionary< YamlNode, int >( ReferenceEqualityComparer.Instance );
        var path = new HashSet< YamlNode >( ReferenceEqualityComparer.Instance );
        CountVisits( root, visits, path, 0 );

        var usedNames = new HashSet< string >( StringComparer.Ordinal );
        foreach ( var (node, count) in visits )
        {
            if ( count > 1 && node.Anchor is { } anchor && IsValidAnchorName( anchor ) && usedNames.Add( anchor ) )
                anchorNames[ node ] = anchor;
        }

        int next = 1;
        foreach ( var (node, count) in visits )
        {
            bool needsAnchor = count > 1 && ( node is not YamlScalar || node.Anchor != null );
            if ( !needsAnchor || anchorNames.ContainsKey( node ) )
                continue;

            string name;
            do
            {
                name = "a" + next++;
            }
            while ( !usedNames.Add( name ) );

            anchorNames[ node ] = name;
        }
    }

    static void CountVisits ( YamlNode node, Dictionary< YamlNode, int > visits, HashSet< YamlNode > path, int depth )
    {
        if ( depth > YamlParser.MAX_DEPTH )
            throw new InvalidOperationException( $"YAML nesting is deeper than { YamlParser.MAX_DEPTH } levels." );

        if ( visits.TryGetValue( node, out int count ) )
        {
            if ( path.Contains( node ) )
                throw new InvalidOperationException( "A YAML node contains itself, so it can't be written." );

            visits[ node ] = count + 1;
            return;
        }

        visits[ node ] = 1;
        path.Add( node );
        switch ( node )
        {
            case YamlList list:
                foreach ( var item in list )
                    CountVisits( item, visits, path, depth + 1 );
                break;
            case YamlMap map:
                foreach ( var (key, value) in map )
                {
                    CountVisits( key, visits, path, depth + 1 );
                    CountVisits( value, visits, path, depth + 1 );
                }
                break;
        }

        path.Remove( node );
    }

    static bool IsValidAnchorName ( string name ) =>
        name.Length > 0 && !name.Any( c => c is ' ' or '\t' or '\n' or '\r' or ',' or '[' or ']' or '{' or '}' );

    static bool IsEmptyPlain ( YamlNode node ) => node is YamlScalar { Style: YamlScalarStyle.Plain, Value: "" };

    void Indent ( int indent ) => output.Append( ' ', indent );

    /// <summary>Returns the node's anchor and tag text, or "*name" if it was already written.</summary>
    string? Properties ( YamlNode node, out bool isAlias )
    {
        isAlias = false;
        string? anchor = null;
        if ( anchorNames.TryGetValue( node, out string? name ) )
        {
            if ( !anchorsWritten.Add( node ) )
            {
                isAlias = true;
                return "*" + name;
            }

            anchor = "&" + name;
        }

        string? tag = node.Tag == null ? null : FormatTag( node.Tag );
        return anchor != null && tag != null ? anchor + " " + tag : anchor ?? tag;
    }

    static string FormatTag ( string tag )
    {
        const string CORE_PREFIX = "tag:yaml.org,2002:";
        if ( tag == YamlScalar.NON_SPECIFIC_TAG )
            return tag;

        if ( tag.StartsWith( CORE_PREFIX, StringComparison.Ordinal ) && IsTagSuffix( tag.AsSpan( CORE_PREFIX.Length ) ) )
            return "!!" + tag[ CORE_PREFIX.Length.. ];

        if ( tag.Length > 1 && tag[ 0 ] == '!' && IsTagSuffix( tag.AsSpan( 1 ) ) )
            return tag;

        if ( tag.Length == 0 || tag.Any( c => c is '>' || char.IsWhiteSpace( c ) || char.IsControl( c ) ) )
            throw new InvalidOperationException( $"The tag '{ tag }' can't be written." );

        return "!<" + tag + ">";
    }

    static bool IsTagSuffix ( ReadOnlySpan< char > suffix )
    {
        if ( suffix.IsEmpty )
            return false;

        foreach ( char c in suffix )
        {
            bool valid = char.IsAsciiLetterOrDigit( c ) || "-_.~:/?#@$&'()*+;=".Contains( c );
            if ( !valid )
                return false;
        }

        return true;
    }

    /// <param name="indent">Indentation of the collection that holds the node (0 at the root).</param>
    void WriteValue ( YamlNode node, int indent, Position position )
    {
        string separator = position == Position.Root ? "" : " ";
        string? properties = Properties( node, out bool isAlias );
        if ( isAlias )
        {
            output.Append( separator ).Append( properties ).Append( '\n' );
            return;
        }

        int childIndent = position == Position.Root ? 0 : indent + INDENT;
        switch ( node )
        {
            case YamlScalar scalar:
                if ( properties != null )
                    output.Append( separator ).Append( properties );

                if ( IsEmptyPlain( scalar ) )
                {
                    output.Append( '\n' );
                    return;
                }

                output.Append( properties != null ? " " : separator );
                WriteScalar( scalar, position == Position.Root ? -1 : indent );
                return;

            case YamlList { Count: 0 }:
            case YamlMap { Count: 0 }:
                if ( properties != null )
                    output.Append( separator ).Append( properties );

                output.Append( properties != null ? " " : separator ).Append( node is YamlList ? "[]\n" : "{}\n" );
                return;

            case YamlList list:
                if ( properties != null || position == Position.MapValue )
                {
                    if ( properties != null )
                        output.Append( separator ).Append( properties );

                    output.Append( '\n' );
                    WriteSequenceEntries( list, properties != null ? childIndent : indent, compactFirst: false );
                    return;
                }

                output.Append( separator );
                WriteSequenceEntries( list, childIndent, compactFirst: position == Position.SequenceItem );
                return;

            case YamlMap map:
                if ( properties != null || position == Position.MapValue )
                {
                    if ( properties != null )
                        output.Append( separator ).Append( properties );

                    output.Append( '\n' );
                    WriteMappingEntries( map, childIndent, compactFirst: false );
                    return;
                }

                output.Append( separator );
                WriteMappingEntries( map, childIndent, compactFirst: position == Position.SequenceItem );
                return;
        }
    }

    void WriteSequenceEntries ( YamlList list, int indent, bool compactFirst )
    {
        for ( int i = 0; i < list.Count; i++ )
        {
            if ( i > 0 || !compactFirst )
                Indent( indent );

            output.Append( '-' );
            WriteValue( list[ i ], indent, Position.SequenceItem );
        }
    }

    void WriteMappingEntries ( YamlMap map, int indent, bool compactFirst )
    {
        for ( int i = 0; i < map.Count; i++ )
        {
            if ( i > 0 || !compactFirst )
                Indent( indent );

            var (key, value) = map[ i ];
            if ( !TryWriteImplicitKey( key ) )
            {
                output.Append( '?' );
                WriteValue( key, indent, Position.SequenceItem );
                Indent( indent );
                output.Append( ':' );
                WriteValue( value, indent, Position.SequenceItem );
                continue;
            }

            output.Append( ':' );
            WriteValue( value, indent, Position.MapValue );
        }
    }

    bool TryWriteImplicitKey ( YamlNode key )
    {
        if ( key is not YamlScalar scalar || IsEmptyPlain( scalar ) )
            return false;

        string? inline = InlineScalar( scalar, isKey: true );
        if ( inline == null )
            return false;

        string? properties = Properties( key, out bool isAlias );
        if ( isAlias )
        {
            output.Append( properties ).Append( ' ' );
            return true;
        }

        if ( properties != null )
            output.Append( properties ).Append( ' ' );

        output.Append( inline );
        return true;
    }

    /// <param name="parentIndent">Indentation of the enclosing collection, or -1 at the root.</param>
    void WriteScalar ( YamlScalar scalar, int parentIndent )
    {
        string? inline = InlineScalar( scalar, isKey: false );
        if ( inline != null )
        {
            output.Append( inline ).Append( '\n' );
            return;
        }

        WriteLiteral( scalar.Value, Math.Max( parentIndent, 0 ) + INDENT );
    }

    /// <summary>The scalar as a single-line token, or null if it should be a literal block.</summary>
    static string? InlineScalar ( YamlScalar scalar, bool isKey )
    {
        string value = scalar.Value;
        bool literal = !isKey && IsLiteralSafe( value );
        switch ( scalar.Style )
        {
            case YamlScalarStyle.Plain:
                if ( IsPlainSafe( value, isKey ) )
                    return value;

                break;
            case YamlScalarStyle.Any:
                if ( IsPlainSafe( value, isKey ) && !YamlScalarResolver.LooksLikeNonString( value ) )
                    return value;

                break;
            case YamlScalarStyle.SingleQuoted:
                if ( IsSingleQuoteSafe( value ) )
                    return "'" + value.Replace( "'", "''" ) + "'";

                break;
            case YamlScalarStyle.Literal or YamlScalarStyle.Folded:
                if ( literal )
                    return null;

                break;
        }

        if ( literal && value.Contains( '\n' ) )
            return null;

        return DoubleQuoted( value );
    }

    static bool IsSpecialCharacter ( char c ) =>
        c < ' ' || c is >= '\u007F' and <= '\u009F' or '\u2028' or '\u2029' or '\uFEFF' or '\uFFFE' or '\uFFFF';

    static bool IsPlainSafe ( string value, bool isKey )
    {
        if ( value.Length == 0 || isKey && value.Length > 1024 )
            return false;

        char first = value[ 0 ];
        if ( first is ' ' or ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`' )
            return false;

        if ( first is '-' or '?' or ':' && ( value.Length == 1 || value[ 1 ] is ' ' ) )
            return false;

        if ( value[ ^1 ] is ' ' or ':' )
            return false;

        if ( value.StartsWith( "---", StringComparison.Ordinal ) || value.StartsWith( "...", StringComparison.Ordinal ) )
            return false;

        for ( int i = 0; i < value.Length; i++ )
        {
            char c = value[ i ];
            if ( IsSpecialCharacter( c ) )
                return false;

            if ( c == ':' && value[ i + 1 ] == ' ' )
                return false;

            if ( c == '#' && value[ i - 1 ] == ' ' )
                return false;
        }

        return true;
    }

    static bool IsSingleQuoteSafe ( string value ) => !value.Any( IsSpecialCharacter );

    static bool IsLiteralSafe ( string value )
    {
        bool hasContent = false;
        foreach ( char c in value )
        {
            if ( c is '\n' )
                continue;

            if ( c != '\t' && IsSpecialCharacter( c ) )
                return false;

            hasContent = true;
        }

        return hasContent;
    }

    static string DoubleQuoted ( string value )
    {
        var builder = new StringBuilder( value.Length + 2 );
        builder.Append( '"' );
        for ( int i = 0; i < value.Length; i++ )
        {
            char c = value[ i ];
            switch ( c )
            {
                case '"': builder.Append( "\\\"" ); break;
                case '\\': builder.Append( "\\\\" ); break;
                case '\n': builder.Append( "\\n" ); break;
                case '\t': builder.Append( "\\t" ); break;
                case '\r': builder.Append( "\\r" ); break;
                case '\0': builder.Append( "\\0" ); break;
                case '\u001B': builder.Append( "\\e" ); break;
                case '\u0085': builder.Append( "\\N" ); break;
                case '\u2028': builder.Append( "\\L" ); break;
                case '\u2029': builder.Append( "\\P" ); break;
                default:
                    if ( c < ' ' || c is >= '\u007F' and <= '\u009F' )
                        builder.Append( "\\x" ).Append( ( ( int )c ).ToString( "X2" ) );
                    else if ( c is '\uFEFF' or '\uFFFE' or '\uFFFF' || char.IsSurrogate( c ) && !IsSurrogatePairAt( value, i ) )
                        builder.Append( "\\u" ).Append( ( ( int )c ).ToString( "X4" ) );
                    else if ( char.IsHighSurrogate( c ) )
                        builder.Append( c ).Append( value[ ++i ] );
                    else
                        builder.Append( c );
                    break;
            }
        }

        return builder.Append( '"' ).ToString();
    }

    static bool IsSurrogatePairAt ( string value, int i ) =>
        char.IsHighSurrogate( value[ i ] ) && i + 1 < value.Length && char.IsLowSurrogate( value[ i + 1 ] );

    void WriteLiteral ( string value, int contentIndent )
    {
        int trailingBreaks = 0;
        while ( trailingBreaks < value.Length && value[ value.Length - 1 - trailingBreaks ] == '\n' )
            trailingBreaks++;

        string[] lines = value[ ..^trailingBreaks ].Split( '\n' );
        string firstContent = lines.First( l => l.Length > 0 );

        output.Append( '|' );
        if ( firstContent[ 0 ] == ' ' )
            output.Append( INDENT );

        if ( trailingBreaks == 0 )
            output.Append( '-' );
        else if ( trailingBreaks > 1 )
            output.Append( '+' );

        output.Append( '\n' );
        foreach ( string line in lines )
        {
            if ( line.Length > 0 )
                Indent( contentIndent );

            output.Append( line ).Append( '\n' );
        }

        output.Append( '\n', Math.Max( trailingBreaks - 1, 0 ) );
    }
}
