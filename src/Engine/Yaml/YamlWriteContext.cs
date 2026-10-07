using System.Diagnostics.CodeAnalysis;

namespace Engine;

/// <summary>State for one serialization. <see cref="IYamlConverter"/> implementations use it to write nested values.</summary>
public sealed class YamlWriteContext
{
    int depth;

    public YamlNode Write ( object? value, [ DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS ) ] Type type ) =>
        YamlSerializer.Write( value, type, this );

    public YamlNode Write< [ DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS ) ] T > ( T value ) =>
        YamlSerializer.Write( value, typeof( T ), this );

    internal void Enter ()
    {
        if ( ++depth > YamlParser.MAX_DEPTH )
            throw new InvalidOperationException( $"Values are nested deeper than { YamlParser.MAX_DEPTH } levels; the object graph may contain a cycle." );
    }

    internal void Exit () => depth--;
}
