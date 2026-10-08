using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Engine;

/// <summary>
/// A node in a parsed (or hand-built) YAML document: a <see cref="YamlScalar"/>, <see cref="YamlList"/>
/// or <see cref="YamlMap"/>. Aliases resolve to the anchored node itself, so a tree can share nodes.
/// </summary>
public abstract class YamlNode
{
    /// <summary>1-based position in the source text; 0 for nodes built in code.</summary>
    public int line { get; init; }

    public int column { get; init; }

    /// <summary>The resolved tag, e.g. <c>tag:yaml.org,2002:str</c> for <c>!!str</c>, or null if untagged.</summary>
    public string? tag { get; set; }

    public string? anchor { get; set; }

    public bool isNull => this is YamlScalar scalar && scalar.isNullValue;

    public abstract string kindName { get; }

    public override string ToString () => YamlWriter.Write( this );
}

public enum YamlScalarStyle
{
    /// <summary>A string; the writer chooses plain when that still reads back as a string, otherwise quoted.</summary>
    Any,
    Plain,
    SingleQuoted,
    DoubleQuoted,
    Literal,
    Folded,
}

public sealed class YamlScalar : YamlNode
{
    public const string STRING_TAG = "tag:yaml.org,2002:str";
    public const string NULL_TAG = "tag:yaml.org,2002:null";
    public const string NON_SPECIFIC_TAG = "!";

    public YamlScalar ( string value, YamlScalarStyle style = YamlScalarStyle.Any )
    {
        this.value = value;
        this.style = style;
    }

    public static YamlScalar Null () => new( "null", YamlScalarStyle.Plain );

    public string value { get; }

    public YamlScalarStyle style { get; }

    public override string kindName => isNullValue ? "null" : "scalar";

    /// <summary>
    /// True for the core schema's null forms (empty, <c>~</c>, <c>null</c>) when they are written plain
    /// and untagged, or for anything tagged <c>!!null</c>.
    /// </summary>
    public bool isNullValue
    {
        get
        {
            if ( tag == NULL_TAG )
                return true;

            return tag == null && isImplicit && YamlScalarResolver.IsNull( value );
        }
    }

    /// <summary>
    /// True when the scalar's type comes from its content (plain and untagged). Every other scalar, including
    /// one built with <see cref="YamlScalarStyle.Any"/>, is a string.
    /// </summary>
    public bool isImplicit => tag == null && style == YamlScalarStyle.Plain;

    public bool TryGetBool ( out bool value ) => YamlScalarResolver.TryParseBool( this.value, out value );

    public bool TryGetInt64 ( out long value ) => YamlScalarResolver.TryParseInt64( this.value, out value );

    public bool TryGetDouble ( out double value ) => YamlScalarResolver.TryParseDouble( this.value, out value );

    /// <summary>
    /// Resolves the scalar with the YAML 1.2 core schema: null, <see cref="bool"/>, <see cref="long"/>,
    /// <see cref="double"/>, or the string itself. Quoted and string-tagged scalars are always strings.
    /// </summary>
    public object? Resolve ()
    {
        if ( isNullValue )
            return null;

        if ( !isImplicit )
            return value;

        if ( TryGetBool( out bool b ) )
            return b;

        if ( TryGetInt64( out long l ) )
            return l;

        if ( YamlScalarResolver.IsFloat( value ) && TryGetDouble( out double d ) )
            return d;

        return value;
    }
}

public sealed class YamlList : YamlNode, IReadOnlyList< YamlNode >
{
    readonly List< YamlNode > items = new();

    public YamlList () { }

    public YamlList ( IEnumerable< YamlNode > items )
    {
        foreach ( var item in items )
            Add( item );
    }

    public override string kindName => "list";

    public int Count => items.Count;

    public YamlNode this[int index]
    {
        get => items[index];
        set => items[index] = value ?? throw new ArgumentNullException( nameof( value ) );
    }

    public void Add ( YamlNode item ) => items.Add( item ?? throw new ArgumentNullException( nameof( item ) ) );

    public void Insert ( int index, YamlNode item ) => items.Insert( index, item ?? throw new ArgumentNullException( nameof( item ) ) );

    public void RemoveAt ( int index ) => items.RemoveAt( index );

    public void Clear () => items.Clear();

    public List< YamlNode >.Enumerator GetEnumerator () => items.GetEnumerator();

    IEnumerator< YamlNode > IEnumerable< YamlNode >.GetEnumerator () => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator () => items.GetEnumerator();
}

/// <summary>
/// An ordered mapping. Keys are usually scalars and can be looked up by their text; scalar keys must be
/// unique. Complex keys (lists or maps) are kept but can only be reached through enumeration.
/// </summary>
public sealed class YamlMap : YamlNode, IReadOnlyList< KeyValuePair< YamlNode, YamlNode > >
{
    readonly List< KeyValuePair< YamlNode, YamlNode > > entries = new();
    readonly Dictionary< string, int > scalarKeyIndices = new( StringComparer.Ordinal );

    public override string kindName => "map";

    public int Count => entries.Count;

    public KeyValuePair< YamlNode, YamlNode > this[int index] => entries[index];

    /// <exception cref="KeyNotFoundException">No scalar key has this text.</exception>
    public YamlNode this[string key]
    {
        get => TryGetValue( key, out var value ) ? value : throw new KeyNotFoundException( $"Key '{key}' was not found." );
        set => Set( key, value );
    }

    public IEnumerable< YamlNode > keys => entries.Select( entry => entry.Key );

    public IEnumerable< YamlNode > values => entries.Select( entry => entry.Value );

    public bool ContainsKey ( string key ) => scalarKeyIndices.ContainsKey( key );

    /// <returns>The entry index of the scalar key, or -1.</returns>
    public int IndexOf ( string key ) => scalarKeyIndices.TryGetValue( key, out int index ) ? index : -1;

    public bool TryGetValue ( string key, [NotNullWhen( true )] out YamlNode? value )
    {
        if ( scalarKeyIndices.TryGetValue( key, out int index ) )
        {
            value = entries[index].Value;
            return true;
        }

        value = null;
        return false;
    }

    /// <exception cref="ArgumentException">A scalar key with the same text already exists.</exception>
    public void Add ( string key, YamlNode value ) => Add( new YamlScalar( key ), value );

    /// <exception cref="ArgumentException">A scalar key with the same text already exists.</exception>
    public void Add ( YamlNode key, YamlNode value )
    {
        ArgumentNullException.ThrowIfNull( key );
        ArgumentNullException.ThrowIfNull( value );

        if ( key is YamlScalar scalar && !scalarKeyIndices.TryAdd( scalar.value, entries.Count ) )
            throw new ArgumentException( $"Duplicate key '{scalar.value}'.", nameof( key ) );

        entries.Add( new( key, value ) );
    }

    /// <summary>Replaces the value of an existing scalar key in place, or adds a new entry.</summary>
    public void Set ( string key, YamlNode value )
    {
        ArgumentNullException.ThrowIfNull( value );

        if ( scalarKeyIndices.TryGetValue( key, out int index ) )
            entries[index] = new( entries[index].Key, value );
        else
            Add( key, value );
    }

    public bool Remove ( string key )
    {
        if ( !scalarKeyIndices.TryGetValue( key, out int index ) )
            return false;

        entries.RemoveAt( index );
        RebuildIndex();
        return true;
    }

    public void Clear ()
    {
        entries.Clear();
        scalarKeyIndices.Clear();
    }

    public List< KeyValuePair< YamlNode, YamlNode > >.Enumerator GetEnumerator () => entries.GetEnumerator();

    IEnumerator< KeyValuePair< YamlNode, YamlNode > > IEnumerable< KeyValuePair< YamlNode, YamlNode > >.GetEnumerator () => entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator () => entries.GetEnumerator();

    void RebuildIndex ()
    {
        scalarKeyIndices.Clear();
        for ( int i = 0; i < entries.Count; i++ )
        {
            if ( entries[i].Key is YamlScalar scalar )
                scalarKeyIndices[scalar.value] = i;
        }
    }
}

/// <summary>The YAML 1.2 core schema's plain-scalar formats.</summary>
public static class YamlScalarResolver
{
    public static bool IsNull ( string value ) => value is "" or "~" or "null" or "Null" or "NULL";

    public static bool TryParseBool ( string value, out bool result )
    {
        switch ( value )
        {
            case "true" or "True" or "TRUE":
                result = true;
                return true;
            case "false" or "False" or "FALSE":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    /// <summary>Decimal, <c>0x</c> hexadecimal or <c>0o</c> octal, with an optional sign.</summary>
    public static bool TryParseInteger ( string value, out bool negative, out ulong magnitude )
    {
        negative = false;
        magnitude = 0;

        int i = 0;
        if ( value.Length > 0 && value[0] is '-' or '+' )
        {
            negative = value[0] == '-';
            i = 1;
        }

        int radix = 10;
        if ( value.Length - i > 2 && value[i] == '0' && value[i + 1] is 'x' or 'o' )
        {
            if ( i != 0 )
                return false;

            radix = value[i + 1] == 'x' ? 16 : 8;
            i += 2;
        }

        if ( i >= value.Length )
            return false;

        for ( ; i < value.Length; i++ )
        {
            int digit = DigitValue( value[i] );
            if ( digit < 0 || digit >= radix )
                return false;

            if ( magnitude > ( ulong.MaxValue - ( ulong )digit ) / ( ulong )radix )
                return false;

            magnitude = magnitude * ( ulong )radix + ( ulong )digit;
        }

        return true;
    }

    public static bool TryParseInt64 ( string value, out long result )
    {
        result = 0;
        if ( !TryParseInteger( value, out bool negative, out ulong magnitude ) )
            return false;

        if ( negative )
        {
            if ( magnitude > ( ulong )long.MaxValue + 1 )
                return false;

            result = unchecked( -( long )magnitude );
            return true;
        }

        if ( magnitude > long.MaxValue )
            return false;

        result = ( long )magnitude;
        return true;
    }

    /// <summary>
    /// <c>[-+]? ( . digits | digits ( . digits? )? ) ( [eE] [-+]? digits )?</c>, or <c>.inf</c> / <c>.nan</c>.
    /// Integers count as floats too.
    /// </summary>
    public static bool IsFloat ( string value )
    {
        if ( IsSpecialFloat( value, out _ ) )
            return true;

        int i = 0;
        if ( i < value.Length && value[i] is '-' or '+' )
            i++;

        int integerDigits = CountDigits( value, ref i );
        int fractionDigits = 0;
        if ( i < value.Length && value[i] == '.' )
        {
            i++;
            fractionDigits = CountDigits( value, ref i );
        }

        if ( integerDigits == 0 && fractionDigits == 0 )
            return false;

        if ( i < value.Length && value[i] is 'e' or 'E' )
        {
            i++;
            if ( i < value.Length && value[i] is '-' or '+' )
                i++;

            if ( CountDigits( value, ref i ) == 0 )
                return false;
        }

        return i == value.Length;
    }

    /// <summary>Parses any core-schema float or integer. Finite text that overflows a double is rejected.</summary>
    public static bool TryParseDouble ( string value, out double result )
    {
        if ( IsSpecialFloat( value, out result ) )
            return true;

        if ( IsFloat( value ) )
        {
            result = double.Parse( value, NumberStyles.Float, CultureInfo.InvariantCulture );
            return double.IsFinite( result );
        }

        if ( TryParseInteger( value, out bool negative, out ulong magnitude ) )
        {
            result = negative ? -( double )magnitude : magnitude;
            return true;
        }

        result = 0;
        return false;
    }

    /// <summary>
    /// True for text that a YAML 1.2 core or YAML 1.1 parser would read as something other than a string when
    /// written plain. YAML 1.1 is checked too (yes/no, on/off, 1_000, 1:30, dates) so other tools read our output the same way.
    /// </summary>
    public static bool LooksLikeNonString ( string value )
    {
        if ( IsNull( value ) || TryParseBool( value, out _ ) || TryParseInteger( value, out _, out _ ) || IsFloat( value ) )
            return true;

        if ( value is "yes" or "Yes" or "YES" or "no" or "No" or "NO" or "on" or "On" or "ON" or "off" or "Off" or "OFF" or "<<" or "=" )
            return true;

        return IsYaml11NumberLike( value );
    }

    static bool IsYaml11NumberLike ( string value )
    {
        int i = value.Length > 0 && value[0] is '-' or '+' ? 1 : 0;
        if ( i >= value.Length || !( char.IsAsciiDigit( value[i] ) || value[i] == '.' ) )
            return false;

        foreach ( char c in value.AsSpan( i ) )
        {
            if ( !char.IsAsciiDigit( c ) && c is not ( '_' or ':' or '.' or '-' or '+' or 'e' or 'E' or 'b' or 'x' ) )
                return false;
        }

        return true;
    }

    static bool IsSpecialFloat ( string value, out double result )
    {
        switch ( value )
        {
            case ".inf" or ".Inf" or ".INF" or "+.inf" or "+.Inf" or "+.INF":
                result = double.PositiveInfinity;
                return true;
            case "-.inf" or "-.Inf" or "-.INF":
                result = double.NegativeInfinity;
                return true;
            case ".nan" or ".NaN" or ".NAN":
                result = double.NaN;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    static int CountDigits ( string value, ref int i )
    {
        int start = i;
        while ( i < value.Length && char.IsAsciiDigit( value[i] ) )
            i++;

        return i - start;
    }

    static int DigitValue ( char c ) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };
}
