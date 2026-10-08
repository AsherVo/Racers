using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Engine;

/// <summary>
/// State for one deserialization: options, the key path being read, and every error found so far. Reading
/// continues past errors so all of a file's problems are reported at once; a value read with errors is discarded.
/// <see cref="IYamlConverter"/> implementations use its typed readers and <see cref="AddError"/>.
/// </summary>
public sealed class YamlReadContext
{
    readonly List< YamlError > errorList = new();
    readonly List< PathSegment > path = new();
    int depth;

    readonly record struct PathSegment ( string? key, int index );

    public YamlReadContext ( YamlReadOptions? options = null, string? source = null )
    {
        this.options = options ?? YamlReadOptions.Default;
        this.source = source;
    }

    public YamlReadOptions options { get; }

    public string? source { get; }

    public IReadOnlyList< YamlError > errors => errorList;

    public bool hasErrors => errorList.Count > 0;

    /// <summary>The key path being read, e.g. <c>rootNode.children[2].speed</c>.</summary>
    public string currentPath
    {
        get
        {
            var builder = new StringBuilder();
            foreach ( var segment in path )
            {
                if ( segment.key == null )
                {
                    builder.Append( '[' ).Append( segment.index ).Append( ']' );
                    continue;
                }

                if ( builder.Length > 0 )
                    builder.Append( '.' );

                builder.Append( segment.key );
            }

            return builder.ToString();
        }
    }

    public void AddError ( YamlNode node, string message ) =>
        errorList.Add( new YamlError( message, node.line, node.column, currentPath, source ) );

    public void PushPath ( string key ) => path.Add( new PathSegment( key, 0 ) );

    public void PushPath ( int index ) => path.Add( new PathSegment( null, index ) );

    public void PopPath () => path.RemoveAt( path.Count - 1 );

    public object? Read ( YamlNode node, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type ) =>
        YamlSerializer.Read( node, type, this );

    public T? Read< [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( YamlNode node ) =>
        ( T? )YamlSerializer.Read( node, typeof( T ), this );

    internal static string Describe ( YamlNode node ) => node switch
    {
        YamlScalar { isNullValue: true } => "null",
        YamlScalar scalar => scalar.value.Length > 40 ? $"'{scalar.value[..40]}...'" : $"'{scalar.value}'",
        YamlList => "a list",
        _ => "a map",
    };

    internal void Expected ( YamlNode node, string expected ) => AddError( node, $"Expected {expected}, found {Describe( node )}." );

    internal bool TryGetText ( YamlNode node, string expected, out string text )
    {
        if ( node is YamlScalar { isNullValue: false } scalar )
        {
            text = scalar.value;
            return true;
        }

        Expected( node, expected );
        text = "";
        return false;
    }

    internal bool Enter ( YamlNode node )
    {
        if ( depth >= YamlParser.MAX_DEPTH )
        {
            AddError( node, $"Nesting is deeper than {YamlParser.MAX_DEPTH} levels." );
            return false;
        }

        depth++;
        return true;
    }

    internal void Exit () => depth--;

    public string? ReadString ( YamlNode node )
    {
        if ( node is YamlScalar scalar )
            return scalar.isNullValue ? null : scalar.value;

        Expected( node, "a string" );
        return null;
    }

    public char ReadChar ( YamlNode node )
    {
        if ( !TryGetText( node, "a single character", out string text ) )
            return default;

        if ( text.Length == 1 )
            return text[0];

        Expected( node, "a single character" );
        return default;
    }

    public bool ReadBool ( YamlNode node )
    {
        if ( TryGetText( node, "true or false", out string text ) )
        {
            if ( YamlScalarResolver.TryParseBool( text, out bool value ) )
                return value;

            Expected( node, "true or false" );
        }

        return false;
    }

    /// <summary>Reads a decimal, <c>0x</c> hexadecimal or <c>0o</c> octal integer between the limits.</summary>
    public Int128 ReadInteger ( YamlNode node, Int128 min, Int128 max )
    {
        if ( !TryGetText( node, "an integer", out string text ) )
            return 0;

        if ( !YamlScalarResolver.TryParseInteger( text, out bool negative, out ulong magnitude ) )
        {
            Expected( node, "an integer" );
            return 0;
        }

        Int128 value = negative ? -( Int128 )magnitude : magnitude;
        if ( value < min || value > max )
        {
            AddError( node, $"{text} is out of range ({min} to {max})." );
            return 0;
        }

        return value;
    }

    public long ReadInt64 ( YamlNode node ) => ( long )ReadInteger( node, long.MinValue, long.MaxValue );

    public int ReadInt32 ( YamlNode node ) => ( int )ReadInteger( node, int.MinValue, int.MaxValue );

    public double ReadDouble ( YamlNode node )
    {
        if ( !TryGetText( node, "a number", out string text ) )
            return 0;

        if ( YamlScalarResolver.TryParseDouble( text, out double value ) )
            return value;

        Expected( node, "a number" );
        return 0;
    }

    public float ReadSingle ( YamlNode node )
    {
        int errorCount = errorList.Count;
        double value = ReadDouble( node );
        float single = ( float )value;
        if ( errorList.Count == errorCount && float.IsInfinity( single ) && !double.IsInfinity( value ) )
        {
            AddError( node, $"{value} is out of range for a float." );
            return 0;
        }

        return single;
    }

    public decimal ReadDecimal ( YamlNode node )
    {
        if ( !TryGetText( node, "a number", out string text ) )
            return 0;

        if ( YamlScalarResolver.TryParseInteger( text, out bool negative, out ulong magnitude ) )
            return negative ? -( decimal )magnitude : magnitude;

        bool finite = char.IsAsciiDigit( text[^1] ) || text[^1] == '.';
        if ( finite && YamlScalarResolver.IsFloat( text )
            && decimal.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value ) )
            return value;

        Expected( node, "a decimal number" );
        return 0;
    }
}
