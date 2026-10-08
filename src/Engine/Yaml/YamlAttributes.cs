using System.Diagnostics.CodeAnalysis;

namespace Engine;

public enum YamlIgnoreCondition
{
    /// <summary>Never read or written.</summary>
    Always,

    /// <summary>Read normally; not written when the value is null.</summary>
    WhenNull,

    /// <summary>Read normally; not written when the value equals its type's default (null, 0, false, ...).</summary>
    WhenDefault,
}

/// <summary>Leaves a public field or property out of YAML, or only out of the output under a condition.</summary>
[AttributeUsage( AttributeTargets.Field | AttributeTargets.Property )]
public sealed class YamlIgnoreAttribute : Attribute
{
    public YamlIgnoreCondition Condition { get; set; } = YamlIgnoreCondition.Always;
}

/// <summary>Includes a member, even a non-public one, optionally under a different key.</summary>
[AttributeUsage( AttributeTargets.Field | AttributeTargets.Property )]
public sealed class YamlMemberAttribute : Attribute
{
    public YamlMemberAttribute ( string? name = null )
    {
        Name = name;
    }

    public string? Name { get; }
}

/// <summary>Reading fails if the key is missing. Members are otherwise optional and keep their initial value.</summary>
[AttributeUsage( AttributeTargets.Field | AttributeTargets.Property )]
public sealed class YamlRequiredAttribute : Attribute
{
}

/// <summary>
/// Makes a class or interface the root of a polymorphic hierarchy. Reading picks the concrete class from a
/// discriminator key (<c>type: Sequence</c>), and writing emits that key first. Every concrete subclass in the
/// root's assembly can be chosen, plus any listed with <see cref="YamlDerivedTypeAttribute"/>.
/// </summary>
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Interface, Inherited = false )]
public sealed class YamlPolymorphicAttribute : Attribute
{
    public YamlPolymorphicAttribute ( string discriminatorKey = "type" )
    {
        DiscriminatorKey = discriminatorKey;
    }

    public string DiscriminatorKey { get; }
}

/// <summary>Adds a subclass from another assembly to a <see cref="YamlPolymorphicAttribute"/> root.</summary>
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = true, Inherited = false )]
public sealed class YamlDerivedTypeAttribute : Attribute
{
    public YamlDerivedTypeAttribute ( [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type derivedType )
    {
        DerivedType = derivedType;
    }

    [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )]
    public Type DerivedType { get; }
}

/// <summary>The discriminator value for this class under a polymorphic root. Defaults to the class name.</summary>
[AttributeUsage( AttributeTargets.Class, Inherited = false )]
public sealed class YamlTypeNameAttribute : Attribute
{
    public YamlTypeNameAttribute ( string name )
    {
        Name = name;
    }

    public string Name { get; }
}

/// <summary>
/// Converts a type, or one member, with an <see cref="IYamlConverter"/> instead of the default mapping, for a
/// custom representation such as a color written as <c>"#FF8800"</c>. The converter needs a parameterless constructor.
/// </summary>
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property )]
public sealed class YamlConverterAttribute : Attribute
{
    public YamlConverterAttribute ( [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type converterType )
    {
        ConverterType = converterType;
    }

    [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )]
    public Type ConverterType { get; }
}

public interface IYamlConverter
{
    /// <summary>
    /// Converts a non-null node. Report problems with <see cref="YamlReadContext.AddError"/>; the result is
    /// discarded once an error is reported.
    /// </summary>
    object? Read ( YamlNode node, Type type, YamlReadContext context );

    /// <summary>Converts a non-null value.</summary>
    YamlNode Write ( object value, Type type, YamlWriteContext context );
}

public sealed record YamlReadOptions
{
    public static readonly YamlReadOptions Default = new();

    /// <summary>When false (the default), keys that don't match a member are errors, which catches typos.</summary>
    public bool AllowUnknownKeys { get; init; }
}
