using System.Diagnostics.CodeAnalysis;

namespace Engine;

/// <summary>
/// Entry points for YAML: parse text into nodes, write nodes as text, and convert objects to and from YAML.
/// <para>
/// Objects map by reflection. Public fields and public get/set properties are included, keyed by member name,
/// base class members first. <see cref="YamlIgnoreAttribute"/> leaves members out, <see cref="YamlMemberAttribute"/>
/// renames them or includes non-public ones, <see cref="YamlRequiredAttribute"/> makes them mandatory, and
/// <see cref="YamlPolymorphicAttribute"/> picks subclasses from a <c>type</c> key. Classes need a parameterless
/// constructor (it can be private). Collections must be <c>T[]</c>, <c>List&lt;T&gt;</c>, <c>HashSet&lt;T&gt;</c>
/// or <c>Dictionary&lt;TKey, TValue&gt;</c>; <c>object</c> members hold arbitrary data and
/// <see cref="YamlNode"/> members hold raw YAML.
/// </para>
/// </summary>
public static class Yaml
{
    /// <summary>Parses a single-document stream. An empty stream gives a null scalar.</summary>
    /// <param name="source">A file name or other origin, used in error messages.</param>
    /// <exception cref="YamlException">The text is not valid YAML or holds more than one document.</exception>
    public static YamlNode Parse ( string text, string? source = null )
    {
        var documents = YamlParser.ParseStream( text, source );
        if ( documents.Count > 1 )
        {
            var second = documents[1];
            throw new YamlException( new YamlError( $"Expected one document, found {documents.Count}.", second.line, second.column, source: source ) );
        }

        return documents.Count == 1 ? documents[0] : new YamlScalar( "", YamlScalarStyle.Plain );
    }

    /// <exception cref="YamlException">The text is not valid YAML.</exception>
    public static List< YamlNode > ParseAll ( string text, string? source = null ) => YamlParser.ParseStream( text, source );

    public static string Write ( YamlNode node ) => YamlWriter.Write( node );

    /// <summary>Writes each node as a document starting with <c>---</c>.</summary>
    public static string WriteAll ( IEnumerable< YamlNode > documents ) => YamlWriter.WriteAll( documents );

    /// <exception cref="YamlException">The text is not valid YAML or doesn't match <typeparamref name="T"/>; it lists every problem found.</exception>
    /// <exception cref="InvalidOperationException"><typeparamref name="T"/> has a member YAML can't represent.</exception>
    public static T Deserialize< [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( string text, YamlReadOptions? options = null, string? source = null ) =>
        ( T )Deserialize( Parse( text, source ), typeof( T ), options, source );

    /// <inheritdoc cref="Deserialize{T}(string, YamlReadOptions?, string?)"/>
    public static T Deserialize< [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( YamlNode node, YamlReadOptions? options = null, string? source = null ) =>
        ( T )Deserialize( node, typeof( T ), options, source );

    /// <inheritdoc cref="Deserialize{T}(string, YamlReadOptions?, string?)"/>
    public static object Deserialize ( YamlNode node, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type, YamlReadOptions? options = null, string? source = null )
    {
        var context = new YamlReadContext( options, source );
        var value = ReadDocument( node, type, context );
        if ( context.hasErrors )
            throw new YamlException( SortedErrors( context ) );

        return value!;
    }

    public static bool TryDeserialize< [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( string text, [MaybeNullWhen( false )] out T value,
        out IReadOnlyList< YamlError > errors, YamlReadOptions? options = null, string? source = null )
    {
        bool success = TryDeserialize( text, typeof( T ), out object? result, out errors, options, source );
        value = success ? ( T )result! : default;
        return success;
    }

    /// <summary>Parses and reads <paramref name="text"/>, collecting syntax errors and mapping errors instead of throwing.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="type"/> has a member YAML can't represent.</exception>
    public static bool TryDeserialize ( string text, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type, [NotNullWhen( true )] out object? value,
        out IReadOnlyList< YamlError > errors, YamlReadOptions? options = null, string? source = null )
    {
        value = null;
        YamlNode node;
        try
        {
            node = Parse( text, source );
        }
        catch ( YamlException exception )
        {
            errors = exception.errors;
            return false;
        }

        var context = new YamlReadContext( options, source );
        var result = ReadDocument( node, type, context );
        errors = SortedErrors( context );
        if ( context.hasErrors )
            return false;

        value = result!;
        return true;
    }

    static List< YamlError > SortedErrors ( YamlReadContext context ) =>
        context.errors.OrderBy( error => error.line ).ThenBy( error => error.column ).ToList();

    static object? ReadDocument ( YamlNode node, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type, YamlReadContext context )
    {
        if ( !node.isNull )
            return context.Read( node, type );

        context.AddError( node, $"Expected a {type.Name}, but the document is empty." );
        return null;
    }

    public static YamlNode ToNode< [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( T value ) => new YamlWriteContext().Write( value );

    public static YamlNode ToNode ( object? value, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type ) => new YamlWriteContext().Write( value, type );

    public static string Serialize< [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] T > ( T value ) => Write( ToNode( value ) );

    public static string Serialize ( object? value, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type ) => Write( ToNode( value, type ) );
}
