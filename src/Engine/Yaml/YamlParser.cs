using System.Globalization;
using System.Text;

namespace Engine;

/// <summary>
/// A recursive-descent YAML 1.2 parser. It handles block and flow collections, every scalar style,
/// multi-line folding, anchors and aliases, tags, directives and multi-document streams. Comments are
/// skipped. The first syntax error throws a <see cref="YamlException"/> with its line and column.
/// </summary>
sealed class YamlParser
{
    public const int MAX_DEPTH = 256;

    const string CORE_TAG_PREFIX = "tag:yaml.org,2002:";

    readonly string text;
    readonly string? source;
    readonly Dictionary< string, YamlNode > anchors = new( StringComparer.Ordinal );
    readonly Dictionary< string, string > tagHandles = new( StringComparer.Ordinal );

    bool seenYamlDirective;
    int pos;
    int line = 1;
    int lineStart;
    int depth;

    YamlParser ( string text, string? source )
    {
        this.text = Normalize( text, source );
        this.source = source;
    }

    public static List< YamlNode > ParseStream ( string text, string? source ) => new YamlParser( text, source ).ParseStream();

    int column => pos - lineStart;

    bool atEnd => pos >= text.Length;

    char current => pos < text.Length ? text[pos] : '\0';

    char PeekAt ( int offset ) => pos + offset < text.Length ? text[pos + offset] : '\0';

    static bool IsBlank ( char c ) => c is ' ' or '\t';

    static bool IsBreakOrEnd ( char c ) => c is '\n' or '\0';

    static bool IsBlankOrEnd ( char c ) => c is ' ' or '\t' or '\n' or '\0';

    static bool IsFlowIndicator ( char c ) => c is ',' or '[' or ']' or '{' or '}';

    // Line breaks become '\n', a leading byte order mark is dropped, and characters YAML forbids are rejected
    // up front, so the rest of the parser only ever sees '\n' and printable text.
    static string Normalize ( string text, string? source )
    {
        int start = text.Length > 0 && text[0] == '\uFEFF' ? 1 : 0;
        var builder = new StringBuilder( text.Length );
        int line = 1;
        int column = 1;
        for ( int i = start; i < text.Length; i++ )
        {
            char c = text[i];
            if ( c == '\r' )
            {
                if ( i + 1 < text.Length && text[i + 1] == '\n' )
                    i++;

                c = '\n';
            }

            if ( c == '\n' )
            {
                line++;
                column = 1;
                builder.Append( c );
                continue;
            }

            bool forbidden = c < ' ' && c != '\t' || c == '\u007F' || c is >= '\u0080' and <= '\u009F' && c != '\u0085' || c is '\uFFFE' or '\uFFFF';
            if ( forbidden )
                throw new YamlException( new YamlError( $"Invalid character U+{( int )c:X4}.", line, column, source: source ) );

            builder.Append( c );
            column++;
        }

        return builder.ToString();
    }

    YamlException Error ( string message ) => Error( message, line, column );

    YamlException Error ( string message, int errorLine, int errorColumn ) =>
        new( new YamlError( message, errorLine, errorColumn + 1, source: source ) );

    void Advance ()
    {
        if ( text[pos] == '\n' )
        {
            line++;
            lineStart = pos + 1;
        }

        pos++;
    }

    void EnterNode ()
    {
        if ( ++depth > MAX_DEPTH )
            throw Error( $"Nesting is deeper than {MAX_DEPTH} levels." );
    }

    ( int Pos, int Line, int LineStart ) Save () => ( pos, line, lineStart );

    void Restore ( ( int Pos, int Line, int LineStart ) state ) => ( pos, line, lineStart ) = state;

    void SkipBlanks ()
    {
        while ( IsBlank( current ) )
            pos++;
    }

    int IndentOfCurrentLine ()
    {
        int i = lineStart;
        while ( i < text.Length && text[i] == ' ' )
            i++;

        return i - lineStart;
    }

    bool IsLineEndOrComment () => IsBreakOrEnd( current ) || current == '#';

    bool IsIndicator ( char indicator ) => current == indicator && IsBlankOrEnd( PeekAt( 1 ) );

    bool AtDocumentMarker () => column == 0 && IsDocumentMarkerAt( pos );

    bool IsDocumentMarkerAt ( int index )
    {
        if ( index + 3 > text.Length )
            return false;

        char c = text[index];
        if ( c != '-' && c != '.' || text[index + 1] != c || text[index + 2] != c )
            return false;

        return index + 3 == text.Length || IsBlankOrEnd( text[index + 3] );
    }

    void SkipComment ()
    {
        if ( pos > lineStart && !IsBlank( text[pos - 1] ) )
            throw Error( "Comments must be separated from other tokens by whitespace." );

        while ( !IsBreakOrEnd( current ) )
            pos++;
    }

    /// <summary>Requires the rest of the line to be blank or a comment, and stops at the line break.</summary>
    void ExpectLineEnd ()
    {
        SkipBlanks();
        if ( current == '#' )
            SkipComment();

        if ( IsBreakOrEnd( current ) )
            return;

        if ( current == ':' && IsBlankOrEnd( PeekAt( 1 ) ) )
            throw Error( "Mapping values are not allowed here." );

        throw Error( $"Unexpected '{current}'." );
    }

    /// <summary>
    /// Skips blanks, comments and line breaks to the next content in block context.
    /// Returns false at the end of the input.
    /// </summary>
    bool SkipToContent ()
    {
        while ( true )
        {
            SkipBlanks();
            if ( current == '#' )
                SkipComment();

            if ( current != '\n' )
                return !atEnd;

            Advance();
        }
    }

    /// <summary>Block collection entries must be indented with spaces; a tab may only separate scalars.</summary>
    void CheckNoTabBefore ( int index )
    {
        for ( int i = index - 1; i >= lineStart && IsBlank( text[i] ); i-- )
        {
            if ( text[i] == '\t' )
                throw Error( "Tabs cannot be used for indentation.", line, i - lineStart );
        }
    }

    List< YamlNode > ParseStream ()
    {
        var documents = new List< YamlNode >();
        bool documentOpen = false;
        while ( SkipToContent() )
        {
            bool hasDirectives = false;
            seenYamlDirective = false;
            ResetTagHandles();
            while ( column == 0 && current == '%' )
            {
                if ( documentOpen )
                    throw Error( "Directives must follow a '...' document end marker." );

                ParseDirective();
                hasDirectives = true;
                if ( !SkipToContent() )
                    throw Error( "Expected '---' after directives." );
            }

            if ( AtDocumentMarker() && current == '.' )
            {
                if ( hasDirectives )
                    throw Error( "Expected '---' after directives." );

                pos += 3;
                ExpectLineEnd();
                documentOpen = false;
                continue;
            }

            bool explicitStart = AtDocumentMarker() && current == '-';
            if ( explicitStart )
                pos += 3;
            else if ( hasDirectives )
                throw Error( "Expected '---' after directives." );
            else if ( documentOpen )
                throw Error( "Unexpected content after the end of the document." );

            documents.Add( ParseDocumentRoot( explicitStart ) );
            anchors.Clear();
            documentOpen = true;

            if ( !SkipToContent() )
                break;

            if ( AtDocumentMarker() )
            {
                if ( current == '.' )
                {
                    pos += 3;
                    ExpectLineEnd();
                    documentOpen = false;
                }

                continue;
            }

            throw Error( "Unexpected content after the end of the document." );
        }

        return documents;
    }

    void ResetTagHandles ()
    {
        tagHandles.Clear();
        tagHandles["!"] = "!";
        tagHandles["!!"] = CORE_TAG_PREFIX;
    }

    void ParseDirective ()
    {
        pos++;
        string name = ScanNonBlank();
        if ( name == "YAML" )
        {
            if ( seenYamlDirective )
                throw Error( "Duplicate %YAML directive." );

            SkipBlanks();
            string version = ScanNonBlank();
            string[] parts = version.Split( '.' );
            if ( parts.Length != 2 || !int.TryParse( parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major )
                || !int.TryParse( parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _ ) )
                throw Error( $"Invalid %YAML version '{version}'." );

            if ( major != 1 )
                throw Error( $"Unsupported YAML version {version}." );

            seenYamlDirective = true;
        }
        else if ( name == "TAG" )
        {
            SkipBlanks();
            string handle = ScanNonBlank();
            if ( handle.Length == 0 || handle[0] != '!' || handle[^1] != '!' )
                throw Error( $"Invalid tag handle '{handle}'." );

            SkipBlanks();
            string prefix = ScanNonBlank();
            if ( prefix.Length == 0 )
                throw Error( "Missing tag prefix." );

            tagHandles[handle] = prefix;
        }
        else
        {
            while ( !IsBreakOrEnd( current ) && !( current == '#' && IsBlank( text[pos - 1] ) ) )
                pos++;
        }

        ExpectLineEnd();
    }

    string ScanNonBlank ()
    {
        int start = pos;
        while ( !IsBlankOrEnd( current ) )
            pos++;

        return text[start..pos];
    }

    YamlNode ParseDocumentRoot ( bool explicitStart )
    {
        if ( explicitStart )
        {
            int markerColumn = column;
            SkipBlanks();
            if ( !IsLineEndOrComment() )
            {
                if ( column == markerColumn )
                    throw Error( "Expected whitespace after '---'." );

                return ParseBlockNode( -1, inline: true );
            }
        }

        if ( !SkipToContent() || AtDocumentMarker() )
            return EmptyScalar( line, column );

        return ParseBlockNode( -1, inline: false );
    }

    YamlScalar EmptyScalar ( int nodeLine, int nodeColumn ) => new( "", YamlScalarStyle.Plain ) { line = nodeLine, column = nodeColumn + 1 };

    void ApplyProperties ( YamlNode node, string? anchor, string? tag, int propertiesLine, int propertiesColumn )
    {
        if ( tag != null )
        {
            if ( node.tag != null )
                throw Error( "A node can only have one tag.", propertiesLine, propertiesColumn );

            node.tag = tag;
        }

        if ( anchor != null )
        {
            if ( node.anchor != null )
                throw Error( "A node can only have one anchor.", propertiesLine, propertiesColumn );

            node.anchor = anchor;
            anchors[anchor] = node;
        }
    }

    /// <summary>
    /// Parses a node in block context, positioned at its first character.
    /// </summary>
    /// <param name="parentIndent">Indentation of the enclosing block collection (-1 at the top level). Content
    /// on following lines must be indented further.</param>
    /// <param name="inline">True when the node shares a line with a mapping key or '---', where a block collection
    /// can't start.</param>
    YamlNode ParseBlockNode ( int parentIndent, bool inline, bool allowIndentlessSequence = false )
    {
        EnterNode();
        var node = ParseBlockNodeContent( parentIndent, inline, allowIndentlessSequence );
        depth--;
        return node;
    }

    YamlNode ParseBlockNodeContent ( int parentIndent, bool inline, bool allowIndentlessSequence )
    {
        int startPos = pos;
        int startLine = line;
        int startColumn = column;
        string? anchor = null;
        string? tag = null;
        bool hasProperties = ParseProperties( ref anchor, ref tag, flow: false );

        if ( hasProperties && IsLineEndOrComment() )
            return ParsePropertiesLineContent( parentIndent, allowIndentlessSequence, anchor, tag, startLine, startColumn );

        char c = current;
        if ( c == '-' && IsBlankOrEnd( PeekAt( 1 ) ) )
        {
            if ( inline || hasProperties )
                throw Error( "A block sequence cannot start on this line." );

            CheckNoTabBefore( pos );
            return ParseBlockSequence( column );
        }

        if ( c == '?' && IsBlankOrEnd( PeekAt( 1 ) ) )
        {
            if ( inline || hasProperties )
                throw Error( "A block mapping cannot start on this line." );

            CheckNoTabBefore( pos );
            return ParseBlockMapping( column, null );
        }

        if ( c == ':' && IsBlankOrEnd( PeekAt( 1 ) ) )
        {
            if ( inline )
                throw Error( "Mapping values are not allowed here." );

            var emptyKey = EmptyScalar( line, column );
            ApplyProperties( emptyKey, anchor, tag, startLine, startColumn );
            return ParseBlockMapping( startColumn, emptyKey );
        }

        if ( c is '|' or '>' )
        {
            var blockScalar = ParseBlockScalar( parentIndent );
            ApplyProperties( blockScalar, anchor, tag, startLine, startColumn );
            return blockScalar;
        }

        int nodeLine = line;
        int nodeColumn = column;
        var candidate = ParseSingleLineNode( parentIndent, hasProperties, out string? plainText );
        if ( plainText != null )
            candidate = new YamlScalar( plainText, YamlScalarStyle.Plain ) { line = nodeLine, column = nodeColumn + 1 };

        SkipBlanks();
        if ( IsIndicator( ':' ) )
        {
            if ( inline )
                throw Error( "Mapping values are not allowed here." );

            if ( line != nodeLine )
                throw Error( "Implicit mapping keys must be on a single line.", nodeLine, nodeColumn );

            CheckNoTabBefore( startPos );
            ApplyProperties( candidate, anchor, tag, startLine, startColumn );
            return ParseBlockMapping( startColumn, candidate );
        }

        if ( plainText != null )
        {
            string folded = ContinuePlain( plainText, parentIndent, flow: false );
            if ( !ReferenceEquals( folded, plainText ) )
                candidate = new YamlScalar( folded, YamlScalarStyle.Plain ) { line = nodeLine, column = nodeColumn + 1 };
        }

        ApplyProperties( candidate, anchor, tag, startLine, startColumn );
        ExpectLineEnd();
        return candidate;
    }

    // Properties alone on a line belong to the node that starts on a following line, if there is one.
    YamlNode ParsePropertiesLineContent ( int parentIndent, bool allowIndentlessSequence, string? anchor, string? tag, int startLine, int startColumn )
    {
        ExpectLineEnd();
        if ( SkipToContent() && !AtDocumentMarker() )
        {
            bool indented = column > parentIndent;
            bool indentlessSequence = allowIndentlessSequence && column == parentIndent && IsIndicator( '-' );
            if ( indented || indentlessSequence )
            {
                var node = indentlessSequence ? ParseBlockSequence( column ) : ParseBlockNode( parentIndent, inline: false );
                ApplyProperties( node, anchor, tag, startLine, startColumn );
                return node;
            }
        }

        var empty = EmptyScalar( startLine, startColumn );
        ApplyProperties( empty, anchor, tag, startLine, startColumn );
        return empty;
    }

    /// <summary>
    /// Parses a flow collection, quoted scalar, alias or the first line of a plain scalar. For plain scalars the
    /// returned node is a placeholder and <paramref name="plainText"/> holds the text.
    /// </summary>
    YamlNode ParseSingleLineNode ( int parentIndent, bool hasProperties, out string? plainText )
    {
        plainText = null;
        int nodeLine = line;
        int nodeColumn = column;
        switch ( current )
        {
            case '[' or '{':
                return ParseFlowCollection( parentIndent );
            case '"':
                return ParseDoubleQuoted( parentIndent );
            case '\'':
                return ParseSingleQuoted( parentIndent );
            case '*':
                if ( hasProperties )
                    throw Error( "An alias cannot have an anchor or tag." );

                return ParseAlias();
        }

        if ( current == '-' && IsBlankOrEnd( PeekAt( 1 ) ) )
            throw Error( "Block sequence entries are not allowed here." );

        if ( current is '|' or '>' )
            throw Error( "A block scalar cannot be a mapping key." );

        if ( !CanStartPlain( flow: false ) )
            throw Error( current == '\0' ? "Unexpected end of input." : $"Unexpected '{current}'." );

        plainText = ScanPlainLine( flow: false );
        return EmptyScalar( nodeLine, nodeColumn );
    }

    YamlMap ParseBlockMapping ( int indent, YamlNode? firstKey )
    {
        var map = new YamlMap { line = firstKey?.line ?? line, column = indent + 1 };
        var key = firstKey;
        while ( true )
        {
            YamlNode value;
            if ( key == null )
                CheckNoTabBefore( pos );

            if ( key == null && IsIndicator( '?' ) )
            {
                pos++;
                key = ParseIndicatorContent( indent, allowIndentlessSequence: true );
                bool hasValue = SkipToContent() && !AtDocumentMarker() && column == indent && IsIndicator( ':' );
                if ( hasValue )
                {
                    pos++;
                    value = ParseIndicatorContent( indent, allowIndentlessSequence: true );
                }
                else
                {
                    value = EmptyScalar( key.line, key.column - 1 );
                }
            }
            else
            {
                if ( key == null && IsIndicator( ':' ) )
                    key = EmptyScalar( line, column );
                else
                    key ??= ParseImplicitKey( indent );

                pos++;
                value = ParseImplicitValue( indent );
            }

            AddEntry( map, key, value );
            key = null;

            if ( !SkipToContent() || AtDocumentMarker() || column < indent )
                return map;

            if ( column > indent )
                throw Error( "Bad indentation of a mapping entry." );
        }
    }

    YamlNode ParseImplicitKey ( int indent )
    {
        EnterNode();
        int startLine = line;
        int startColumn = column;
        string? anchor = null;
        string? tag = null;
        bool hasProperties = ParseProperties( ref anchor, ref tag, flow: false );
        if ( hasProperties && IsLineEndOrComment() )
            throw Error( "Expected a mapping key after the anchor or tag." );

        var key = ParseSingleLineNode( indent, hasProperties, out string? plainText );
        if ( plainText != null )
            key = new YamlScalar( plainText, YamlScalarStyle.Plain ) { line = key.line, column = key.column };

        SkipBlanks();
        if ( line != startLine )
            throw Error( "Implicit mapping keys must be on a single line.", startLine, startColumn );

        if ( !IsIndicator( ':' ) )
        {
            if ( plainText != null && IsLineEndOrComment() )
                throw Error( $"Expected ':' after '{plainText}'. Every line in a mapping needs a key.", startLine, startColumn );

            throw Error( "Expected ':' after a mapping key." );
        }

        ApplyProperties( key, anchor, tag, startLine, startColumn );
        depth--;
        return key;
    }

    YamlNode ParseImplicitValue ( int indent )
    {
        int valueLine = line;
        int valueColumn = column;
        SkipBlanks();
        if ( !IsLineEndOrComment() )
            return ParseBlockNode( indent, inline: true, allowIndentlessSequence: true );

        ExpectLineEnd();
        if ( !SkipToContent() || AtDocumentMarker() )
            return EmptyScalar( valueLine, valueColumn );

        if ( column > indent )
            return ParseBlockNode( indent, inline: false, allowIndentlessSequence: true );

        if ( column == indent && IsIndicator( '-' ) )
            return ParseBlockSequence( indent );

        return EmptyScalar( valueLine, valueColumn );
    }

    /// <summary>Parses what follows a '-', '?' or explicit ':' indicator, on the same line or indented below.</summary>
    YamlNode ParseIndicatorContent ( int indent, bool allowIndentlessSequence = false )
    {
        int indicatorLine = line;
        int indicatorColumn = column - 1;
        SkipBlanks();
        if ( !IsLineEndOrComment() )
            return ParseBlockNode( indent, inline: false );

        ExpectLineEnd();
        if ( !SkipToContent() || AtDocumentMarker() )
            return EmptyScalar( indicatorLine, indicatorColumn );

        if ( allowIndentlessSequence && column == indent && IsIndicator( '-' ) )
            return ParseBlockSequence( indent );

        if ( column <= indent )
            return EmptyScalar( indicatorLine, indicatorColumn );

        return ParseBlockNode( indent, inline: false, allowIndentlessSequence );
    }

    YamlList ParseBlockSequence ( int indent )
    {
        var list = new YamlList { line = line, column = indent + 1 };
        while ( true )
        {
            CheckNoTabBefore( pos );
            pos++;
            list.Add( ParseIndicatorContent( indent ) );

            if ( !SkipToContent() || AtDocumentMarker() || column < indent )
                return list;

            if ( column > indent )
                throw Error( "Bad indentation of a sequence entry." );

            if ( !IsIndicator( '-' ) )
                return list;
        }
    }

    void AddEntry ( YamlMap map, YamlNode key, YamlNode value )
    {
        if ( key is YamlScalar scalar && map.ContainsKey( scalar.value ) )
            throw Error( $"Duplicate key '{scalar.value}'.", key.line, key.column - 1 );

        map.Add( key, value );
    }

    bool ParseProperties ( ref string? anchor, ref string? tag, bool flow )
    {
        bool any = false;
        while ( current is '&' or '!' )
        {
            int propertyLine = line;
            int propertyColumn = column;
            if ( current == '&' )
            {
                if ( anchor != null )
                    throw Error( "A node can only have one anchor." );

                pos++;
                anchor = ScanAnchorName();
            }
            else
            {
                if ( tag != null )
                    throw Error( "A node can only have one tag." );

                tag = ScanTag();
            }

            if ( !IsBlankOrEnd( current ) && !( flow && IsFlowIndicator( current ) ) )
                throw Error( "Expected whitespace after an anchor or tag.", propertyLine, propertyColumn );

            SkipBlanks();
            any = true;
        }

        return any;
    }

    string ScanAnchorName ()
    {
        int start = pos;
        while ( !IsBlankOrEnd( current ) && !IsFlowIndicator( current ) )
            pos++;

        if ( pos == start )
            throw Error( "Expected an anchor name." );

        return text[start..pos];
    }

    YamlNode ParseAlias ()
    {
        int aliasLine = line;
        int aliasColumn = column;
        pos++;
        string name = ScanAnchorName();
        if ( !anchors.TryGetValue( name, out var node ) )
            throw Error( $"Undefined alias '*{name}'.", aliasLine, aliasColumn );

        return node;
    }

    string ScanTag ()
    {
        int tagLine = line;
        int tagColumn = column;
        pos++;
        if ( current == '<' )
        {
            pos++;
            int start = pos;
            while ( current != '>' )
            {
                if ( IsBlankOrEnd( current ) )
                    throw Error( "Unterminated verbatim tag.", tagLine, tagColumn );

                pos++;
            }

            string verbatim = text[start..pos];
            pos++;
            if ( verbatim.Length == 0 || verbatim == "!" )
                throw Error( "Invalid verbatim tag.", tagLine, tagColumn );

            return DecodeUri( verbatim, tagLine, tagColumn );
        }

        string handle = "!";
        int wordStart = pos;
        while ( char.IsAsciiLetterOrDigit( current ) || current == '-' )
            pos++;

        if ( current == '!' )
        {
            handle = "!" + text[wordStart..pos] + "!";
            pos++;
        }
        else
        {
            pos = wordStart;
        }

        int suffixStart = pos;
        while ( !IsBlankOrEnd( current ) && !IsFlowIndicator( current ) )
        {
            if ( current == '!' )
                throw Error( "Invalid '!' in tag.", tagLine, tagColumn );

            pos++;
        }

        string suffix = text[suffixStart..pos];
        if ( handle == "!" && suffix.Length == 0 )
            return YamlScalar.NON_SPECIFIC_TAG;

        if ( !tagHandles.TryGetValue( handle, out string? prefix ) )
            throw Error( $"Undefined tag handle '{handle}'.", tagLine, tagColumn );

        if ( suffix.Length == 0 )
            throw Error( "Expected a tag suffix.", tagLine, tagColumn );

        return prefix + DecodeUri( suffix, tagLine, tagColumn );
    }

    string DecodeUri ( string uri, int uriLine, int uriColumn )
    {
        if ( !uri.Contains( '%' ) )
            return uri;

        var bytes = new List< byte >();
        var builder = new StringBuilder();
        for ( int i = 0; i < uri.Length; i++ )
        {
            if ( uri[i] == '%' )
            {
                if ( i + 2 >= uri.Length || !byte.TryParse( uri.AsSpan( i + 1, 2 ), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b ) )
                    throw Error( "Invalid '%' escape in tag.", uriLine, uriColumn );

                bytes.Add( b );
                i += 2;
                continue;
            }

            FlushBytes();
            builder.Append( uri[i] );
        }

        FlushBytes();
        return builder.ToString();

        void FlushBytes ()
        {
            if ( bytes.Count == 0 )
                return;

            builder.Append( Encoding.UTF8.GetString( bytes.ToArray() ) );
            bytes.Clear();
        }
    }

    bool CanStartPlain ( bool flow )
    {
        char c = current;
        if ( IsBlankOrEnd( c ) )
            return false;

        if ( c is '-' or '?' or ':' )
        {
            char next = PeekAt( 1 );
            return !IsBlankOrEnd( next ) && !( flow && IsFlowIndicator( next ) );
        }

        return c is not ( ',' or '[' or ']' or '{' or '}' or '#' or '&' or '*' or '!' or '|' or '>' or '\'' or '"' or '%' or '@' or '`' );
    }

    /// <summary>Scans one line of a plain scalar, leaving the position after its last non-blank character.</summary>
    string ScanPlainLine ( bool flow )
    {
        int start = pos;
        int end = pos;
        while ( true )
        {
            char c = current;
            if ( IsBreakOrEnd( c ) )
                break;

            if ( c == ':' && ( IsBlankOrEnd( PeekAt( 1 ) ) || flow && IsFlowIndicator( PeekAt( 1 ) ) ) )
                break;

            if ( c == '#' && pos > start && IsBlank( text[pos - 1] ) )
                break;

            if ( flow && IsFlowIndicator( c ) )
                break;

            pos++;
            if ( !IsBlank( c ) )
                end = pos;
        }

        pos = end;
        return text[start..end];
    }

    /// <summary>
    /// Continues a plain scalar onto following lines that are indented past <paramref name="parentIndent"/>,
    /// folding single line breaks into spaces. Returns <paramref name="firstLine"/> itself if nothing follows.
    /// </summary>
    string ContinuePlain ( string firstLine, int parentIndent, bool flow )
    {
        StringBuilder? builder = null;
        while ( true )
        {
            var state = Save();
            SkipBlanks();
            if ( current != '\n' )
            {
                Restore( state );
                break;
            }

            int breaks = 0;
            while ( current == '\n' )
            {
                Advance();
                breaks++;
                SkipBlanks();
            }

            bool ends = atEnd || current == '#' || IndentOfCurrentLine() <= parentIndent || IsDocumentMarkerAt( lineStart );
            string next = ends ? "" : ScanPlainLine( flow );
            if ( next.Length == 0 )
            {
                Restore( state );
                break;
            }

            builder ??= new StringBuilder( firstLine );
            if ( breaks == 1 )
                builder.Append( ' ' );
            else
                builder.Append( '\n', breaks - 1 );

            builder.Append( next );
        }

        return builder?.ToString() ?? firstLine;
    }

    YamlScalar ParseDoubleQuoted ( int parentIndent )
    {
        int startLine = line;
        int startColumn = column;
        pos++;
        var builder = new StringBuilder();
        int keep = 0;
        while ( true )
        {
            if ( atEnd )
                throw Error( "Unterminated double-quoted string.", startLine, startColumn );

            char c = current;
            if ( c == '"' )
            {
                pos++;
                break;
            }

            if ( c == '\\' )
            {
                if ( PeekAt( 1 ) == '\n' )
                {
                    pos++;
                    Advance();
                    SkipQuotedLinePrefix( parentIndent, startLine, startColumn );
                    while ( current == '\n' )
                    {
                        Advance();
                        builder.Append( '\n' );
                        SkipQuotedLinePrefix( parentIndent, startLine, startColumn );
                    }

                    keep = builder.Length;
                    continue;
                }

                AppendEscape( builder );
                keep = builder.Length;
                continue;
            }

            if ( c == '\n' )
            {
                builder.Length = keep;
                FoldQuotedLines( builder, parentIndent, startLine, startColumn );
                keep = builder.Length;
                continue;
            }

            builder.Append( c );
            pos++;
            if ( !IsBlank( c ) )
                keep = builder.Length;
        }

        return new YamlScalar( builder.ToString(), YamlScalarStyle.DoubleQuoted ) { line = startLine, column = startColumn + 1 };
    }

    void AppendEscape ( StringBuilder builder )
    {
        int escapeLine = line;
        int escapeColumn = column;
        char e = PeekAt( 1 );
        pos += 2;
        switch ( e )
        {
            case '0': builder.Append( '\0' ); break;
            case 'a': builder.Append( '\a' ); break;
            case 'b': builder.Append( '\b' ); break;
            case 't' or '\t': builder.Append( '\t' ); break;
            case 'n': builder.Append( '\n' ); break;
            case 'v': builder.Append( '\v' ); break;
            case 'f': builder.Append( '\f' ); break;
            case 'r': builder.Append( '\r' ); break;
            case 'e': builder.Append( '\u001B' ); break;
            case ' ': builder.Append( ' ' ); break;
            case '"': builder.Append( '"' ); break;
            case '/': builder.Append( '/' ); break;
            case '\\': builder.Append( '\\' ); break;
            case 'N': builder.Append( '\u0085' ); break;
            case '_': builder.Append( '\u00A0' ); break;
            case 'L': builder.Append( '\u2028' ); break;
            case 'P': builder.Append( '\u2029' ); break;
            case 'x': AppendCodePoint( builder, 2, escapeLine, escapeColumn ); break;
            case 'u': AppendCodePoint( builder, 4, escapeLine, escapeColumn ); break;
            case 'U': AppendCodePoint( builder, 8, escapeLine, escapeColumn ); break;
            default: throw Error( $"Invalid escape '\\{e}'.", escapeLine, escapeColumn );
        }
    }

    void AppendCodePoint ( StringBuilder builder, int digits, int escapeLine, int escapeColumn )
    {
        if ( pos + digits > text.Length
            || !int.TryParse( text.AsSpan( pos, digits ), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int codePoint ) )
            throw Error( $"Expected {digits} hexadecimal digits in escape.", escapeLine, escapeColumn );

        pos += digits;
        if ( codePoint > 0x10FFFF )
            throw Error( "Escaped code point is out of range.", escapeLine, escapeColumn );

        if ( codePoint < 0x10000 )
            builder.Append( ( char )codePoint );
        else
            builder.Append( char.ConvertFromUtf32( codePoint ) );
    }

    YamlScalar ParseSingleQuoted ( int parentIndent )
    {
        int startLine = line;
        int startColumn = column;
        pos++;
        var builder = new StringBuilder();
        int keep = 0;
        while ( true )
        {
            if ( atEnd )
                throw Error( "Unterminated single-quoted string.", startLine, startColumn );

            char c = current;
            if ( c == '\'' )
            {
                if ( PeekAt( 1 ) != '\'' )
                {
                    pos++;
                    break;
                }

                builder.Append( '\'' );
                pos += 2;
                keep = builder.Length;
                continue;
            }

            if ( c == '\n' )
            {
                builder.Length = keep;
                FoldQuotedLines( builder, parentIndent, startLine, startColumn );
                keep = builder.Length;
                continue;
            }

            builder.Append( c );
            pos++;
            if ( !IsBlank( c ) )
                keep = builder.Length;
        }

        return new YamlScalar( builder.ToString(), YamlScalarStyle.SingleQuoted ) { line = startLine, column = startColumn + 1 };
    }

    // A single line break inside a quoted scalar folds to a space; each further empty line is a '\n'.
    void FoldQuotedLines ( StringBuilder builder, int parentIndent, int startLine, int startColumn )
    {
        int breaks = 0;
        while ( current == '\n' )
        {
            Advance();
            breaks++;
            SkipQuotedLinePrefix( parentIndent, startLine, startColumn );
        }

        if ( breaks == 1 )
            builder.Append( ' ' );
        else
            builder.Append( '\n', breaks - 1 );
    }

    void SkipQuotedLinePrefix ( int parentIndent, int startLine, int startColumn )
    {
        if ( IsDocumentMarkerAt( pos ) )
            throw Error( "Document marker inside a quoted string.", startLine, startColumn );

        SkipBlanks();
        if ( atEnd )
            throw Error( "Unterminated quoted string.", startLine, startColumn );

        if ( current != '\n' && IndentOfCurrentLine() <= parentIndent )
            throw Error( "Quoted string continuation lines must be indented." );
    }

    YamlScalar ParseBlockScalar ( int parentIndent )
    {
        int startLine = line;
        int startColumn = column;
        bool literal = current == '|';
        pos++;

        int indentIndicator = 0;
        char chomping = ' ';
        for ( int i = 0; i < 2; i++ )
        {
            char c = current;
            if ( c is >= '1' and <= '9' && indentIndicator == 0 )
            {
                indentIndicator = c - '0';
                pos++;
            }
            else if ( c is '+' or '-' && chomping == ' ' )
            {
                chomping = c;
                pos++;
            }
            else if ( c == '0' )
            {
                throw Error( "A block scalar indentation indicator must be 1-9." );
            }
        }

        if ( !IsBlankOrEnd( current ) )
            throw Error( $"Invalid block scalar header character '{current}'." );

        ExpectLineEnd();

        int contentIndent = indentIndicator > 0 ? Math.Max( parentIndent, 0 ) + indentIndicator : -1;
        var lines = new List< string >();
        int maxLeadingEmptyIndent = 0;
        while ( !atEnd )
        {
            Advance();
            if ( atEnd )
                break;

            int spaces = IndentOfCurrentLine();
            int lineEnd = text.IndexOf( '\n', lineStart );
            if ( lineEnd < 0 )
                lineEnd = text.Length;

            bool onlySpaces = lineStart + spaces == lineEnd;
            if ( contentIndent < 0 )
            {
                if ( onlySpaces )
                {
                    maxLeadingEmptyIndent = Math.Max( maxLeadingEmptyIndent, spaces );
                    lines.Add( "" );
                    pos = lineEnd;
                    continue;
                }

                if ( spaces <= parentIndent || IsDocumentMarkerAt( lineStart ) )
                {
                    if ( text[lineStart + spaces] == '\t' && text.AsSpan( lineStart, lineEnd - lineStart ).Trim( " \t" ).IsEmpty )
                        throw Error( "Tabs cannot be used for indentation." );

                    pos = lineStart;
                    break;
                }

                contentIndent = spaces;
                if ( maxLeadingEmptyIndent > contentIndent )
                    throw Error( "Leading empty lines of a block scalar are indented more than its content.", startLine, startColumn );
            }

            if ( spaces < contentIndent || IsDocumentMarkerAt( lineStart ) )
            {
                if ( onlySpaces )
                {
                    lines.Add( "" );
                    pos = lineEnd;
                    continue;
                }

                pos = lineStart;
                break;
            }

            lines.Add( text[( lineStart + contentIndent )..lineEnd] );
            pos = lineEnd;
        }

        string value = literal ? JoinLiteral( lines, chomping ) : JoinFolded( lines, chomping );
        return new YamlScalar( value, literal ? YamlScalarStyle.Literal : YamlScalarStyle.Folded ) { line = startLine, column = startColumn + 1 };
    }

    static int LastContentLine ( List< string > lines )
    {
        for ( int i = lines.Count - 1; i >= 0; i-- )
        {
            if ( lines[i].Length > 0 )
                return i;
        }

        return -1;
    }

    static string Chomp ( StringBuilder builder, List< string > lines, int lastContent, char chomping )
    {
        int trailingEmpty = lines.Count - 1 - lastContent;
        if ( lastContent < 0 )
            return chomping == '+' ? new string( '\n', lines.Count ) : "";

        if ( chomping == '+' )
            builder.Append( '\n', trailingEmpty + 1 );
        else if ( chomping == ' ' )
            builder.Append( '\n' );

        return builder.ToString();
    }

    static string JoinLiteral ( List< string > lines, char chomping )
    {
        int lastContent = LastContentLine( lines );
        var builder = new StringBuilder();
        for ( int i = 0; i <= lastContent; i++ )
        {
            if ( i > 0 )
                builder.Append( '\n' );

            builder.Append( lines[i] );
        }

        return Chomp( builder, lines, lastContent, chomping );
    }

    // Folding joins adjacent text lines with a space. Empty lines become '\n's, and lines that start with
    // whitespace ("more indented") keep their line breaks.
    static string JoinFolded ( List< string > lines, char chomping )
    {
        int lastContent = LastContentLine( lines );
        var builder = new StringBuilder();
        int emptyLines = 0;
        bool seenContent = false;
        bool previousMoreIndented = false;
        for ( int i = 0; i <= lastContent; i++ )
        {
            string current = lines[i];
            if ( current.Length == 0 )
            {
                emptyLines++;
                continue;
            }

            bool moreIndented = IsBlank( current[0] );
            if ( !seenContent )
            {
                builder.Append( '\n', emptyLines );
            }
            else if ( moreIndented || previousMoreIndented )
            {
                builder.Append( '\n', emptyLines + 1 );
            }
            else if ( emptyLines == 0 )
            {
                builder.Append( ' ' );
            }
            else
            {
                builder.Append( '\n', emptyLines );
            }

            builder.Append( current );
            emptyLines = 0;
            seenContent = true;
            previousMoreIndented = moreIndented;
        }

        return Chomp( builder, lines, lastContent, chomping );
    }

    YamlNode ParseFlowCollection ( int blockIndent )
    {
        EnterNode();
        int startLine = line;
        int startColumn = column;
        bool isSequence = current == '[';
        char close = isSequence ? ']' : '}';
        YamlNode collection = isSequence
            ? new YamlList { line = startLine, column = startColumn + 1 }
            : new YamlMap { line = startLine, column = startColumn + 1 };
        pos++;

        while ( true )
        {
            SkipFlowSpace( blockIndent, startLine, startColumn );
            if ( current == close )
            {
                pos++;
                break;
            }

            ParseFlowEntry( collection, close, blockIndent, startLine, startColumn );
            SkipFlowSpace( blockIndent, startLine, startColumn );
            if ( current == ',' )
            {
                pos++;
                continue;
            }

            if ( current == close )
            {
                pos++;
                break;
            }

            throw Error( $"Expected ',' or '{close}' in flow collection." );
        }

        depth--;
        return collection;
    }

    void SkipFlowSpace ( int blockIndent, int startLine, int startColumn )
    {
        while ( true )
        {
            SkipBlanks();
            if ( current == '#' )
                SkipComment();

            if ( current != '\n' )
                break;

            Advance();
            if ( IsDocumentMarkerAt( pos ) )
                throw Error( "Unterminated flow collection.", startLine, startColumn );

            // A closing bracket may sit at the block's indentation, JSON style.
            SkipBlanks();
            bool closing = current is ']' or '}';
            if ( !IsLineEndOrComment() && !closing && IndentOfCurrentLine() <= blockIndent )
                throw Error( "Flow collection lines must be indented more than the enclosing block." );
        }

        if ( atEnd )
            throw Error( "Unterminated flow collection.", startLine, startColumn );
    }

    void ParseFlowEntry ( YamlNode collection, char close, int blockIndent, int startLine, int startColumn )
    {
        int entryLine = line;
        int entryColumn = column;
        YamlNode key;
        YamlNode? value = null;

        if ( current == '?' && ( IsBlankOrEnd( PeekAt( 1 ) ) || IsFlowIndicator( PeekAt( 1 ) ) ) )
        {
            pos++;
            SkipFlowSpace( blockIndent, startLine, startColumn );
            key = IsFlowValueIndicator() || current == ',' || current == close
                ? EmptyScalar( line, column )
                : ParseFlowNode( blockIndent, startLine, startColumn, out _ );

            SkipFlowSpace( blockIndent, startLine, startColumn );
            value = ParseFlowValue( close, blockIndent, startLine, startColumn, adjacent: false ) ?? EmptyScalar( line, column );
        }
        else if ( current == ',' )
        {
            throw Error( "Unexpected ',' in flow collection." );
        }
        else
        {
            bool emptyKey = IsFlowValueIndicator();
            bool adjacent = false;
            key = emptyKey ? EmptyScalar( line, column ) : ParseFlowNode( blockIndent, startLine, startColumn, out adjacent );
            SkipFlowSpace( blockIndent, startLine, startColumn );
            if ( collection is YamlList && current == ':' && line != entryLine )
                throw Error( "Implicit keys in a flow sequence must be on a single line." );

            value = ParseFlowValue( close, blockIndent, startLine, startColumn, adjacent );
        }

        if ( collection is YamlList list )
        {
            if ( value == null )
            {
                list.Add( key );
                return;
            }

            var pair = new YamlMap { line = entryLine, column = entryColumn + 1 };
            pair.Add( key, value );
            list.Add( pair );
            return;
        }

        AddEntry( ( YamlMap )collection, key, value ?? EmptyScalar( key.line, key.column - 1 ) );
    }

    bool IsFlowValueIndicator () => current == ':' && ( IsBlankOrEnd( PeekAt( 1 ) ) || IsFlowIndicator( PeekAt( 1 ) ) );

    /// <summary>Parses ": value" if present. A ':' right after a quoted or flow key needs no space ("{"a":1}").</summary>
    YamlNode? ParseFlowValue ( char close, int blockIndent, int startLine, int startColumn, bool adjacent )
    {
        if ( !( IsFlowValueIndicator() || adjacent && current == ':' ) )
            return null;

        pos++;
        SkipFlowSpace( blockIndent, startLine, startColumn );
        if ( current == ',' || current == close )
            return EmptyScalar( line, column );

        return ParseFlowNode( blockIndent, startLine, startColumn, out _ );
    }

    YamlNode ParseFlowNode ( int blockIndent, int startLine, int startColumn, out bool allowsAdjacentValue )
    {
        EnterNode();
        int propertiesLine = line;
        int propertiesColumn = column;
        string? anchor = null;
        string? tag = null;
        bool hasProperties = ParseProperties( ref anchor, ref tag, flow: true );
        if ( hasProperties )
            SkipFlowSpace( blockIndent, startLine, startColumn );

        allowsAdjacentValue = current is '"' or '\'' or '[' or '{';
        int nodeLine = line;
        int nodeColumn = column;
        YamlNode node;
        switch ( current )
        {
            case '[' or '{':
                node = ParseFlowCollection( blockIndent );
                break;
            case '"':
                node = ParseDoubleQuoted( blockIndent );
                break;
            case '\'':
                node = ParseSingleQuoted( blockIndent );
                break;
            case '*':
                if ( hasProperties )
                    throw Error( "An alias cannot have an anchor or tag." );

                node = ParseAlias();
                break;
            default:
                if ( hasProperties && ( current is ',' or ']' or '}' || IsFlowValueIndicator() ) )
                {
                    node = EmptyScalar( propertiesLine, propertiesColumn );
                }
                else if ( CanStartPlain( flow: true ) )
                {
                    string first = ScanPlainLine( flow: true );
                    string value = ContinuePlain( first, blockIndent, flow: true );
                    node = new YamlScalar( value, YamlScalarStyle.Plain ) { line = nodeLine, column = nodeColumn + 1 };
                }
                else
                {
                    throw Error( $"Unexpected '{current}' in flow collection." );
                }

                break;
        }

        ApplyProperties( node, anchor, tag, propertiesLine, propertiesColumn );
        depth--;
        return node;
    }
}
