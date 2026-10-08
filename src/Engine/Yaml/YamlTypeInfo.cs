using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Engine;

enum YamlTypeKind
{
    String,
    Char,
    Bool,
    Integer,
    Single,
    Double,
    Decimal,
    Enum,
    Flags,
    Nullable,
    Array,
    List,
    Set,
    Dictionary,
    Node,
    Any,
    Converter,
    Object,
}

/// <summary>
/// How a type maps to YAML, worked out once per type with reflection and cached. Unsupported types throw
/// <see cref="InvalidOperationException"/> the first time they're used, naming the type and member.
/// </summary>
sealed class YamlTypeInfo
{
    /// <summary>
    /// Everything reflection may touch on a mapped type. Trimmed builds must also keep the types reached through
    /// member signatures, so hosts root the game and engine assemblies (hosts/Directory.Build.props).
    /// </summary>
    public const DynamicallyAccessedMemberTypes MEMBERS = DynamicallyAccessedMemberTypes.All;

    const string TRIM_JUSTIFICATION = "Types reached through member signatures are kept by rooting the game and engine assemblies.";

    const BindingFlags DECLARED_MEMBERS = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    static readonly ConcurrentDictionary< Type, YamlTypeInfo > Cache = new();
    static readonly ConcurrentDictionary< Type, IYamlConverter > Converters = new();

    readonly Lazy< YamlMemberInfo[] > lazyMembers;
    readonly Lazy< YamlPolymorphism? > lazyPolymorphism;

    public static YamlTypeInfo Get ( [DynamicallyAccessedMembers( MEMBERS )] Type type )
    {
        if ( Cache.TryGetValue( type, out var info ) )
            return info;

        return Cache.GetOrAdd( type, new YamlTypeInfo( type ) );
    }

    [UnconditionalSuppressMessage( "Trimming", "IL2062", Justification = TRIM_JUSTIFICATION )]
    [UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = TRIM_JUSTIFICATION )]
    YamlTypeInfo ( [DynamicallyAccessedMembers( MEMBERS )] Type type )
    {
        this.type = type;
        lazyMembers = new Lazy< YamlMemberInfo[] >( BuildMembers );
        lazyPolymorphism = new Lazy< YamlPolymorphism? >( () => YamlPolymorphism.Find( this.type ) );
        kind = DetermineKind( type );
    }

    [DynamicallyAccessedMembers( MEMBERS )]
    public Type type { get; }

    public YamlTypeKind kind { get; private set; }

    /// <summary>The item type of an array, list or set; the value type of a dictionary; the underlying type of a nullable.</summary>
    [DynamicallyAccessedMembers( MEMBERS )]
    public Type? elementType { get; private set; }

    [DynamicallyAccessedMembers( MEMBERS )]
    public Type? keyType { get; private set; }

    /// <summary>What to store when an item fails to read: null, or a zeroed value for value types.</summary>
    public object? elementDefault { get; private set; }

    public IYamlConverter? converter { get; private set; }

    public Int128 min { get; private set; }

    public Int128 max { get; private set; }

    public MethodInfo? setAdd { get; private set; }

    public YamlMemberInfo[] members => lazyMembers.Value;

    public YamlPolymorphism? polymorphism => lazyPolymorphism.Value;

    public string description => kind switch
    {
        YamlTypeKind.String => "a string",
        YamlTypeKind.Char => "a single character",
        YamlTypeKind.Bool => "true or false",
        YamlTypeKind.Integer => "an integer",
        YamlTypeKind.Single or YamlTypeKind.Double or YamlTypeKind.Decimal => "a number",
        YamlTypeKind.Array or YamlTypeKind.List or YamlTypeKind.Set => "a list",
        YamlTypeKind.Dictionary => "a map",
        _ => "a " + type.Name,
    };

    /// <summary>Creates an empty instance, or throws for types that can't be created (abstract, no parameterless constructor).</summary>
    [UnconditionalSuppressMessage( "AOT", "IL3050", Justification = "Only value types reach Activator without a constructor." )]
    public object CreateInstance ()
    {
        if ( type.IsAbstract || type.IsInterface )
            throw new InvalidOperationException( $"{type.FullName} is abstract, so it can't be created from YAML." );

        if ( type.IsValueType )
            return Activator.CreateInstance( type )!;

        var constructor = type.GetConstructor( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes )
            ?? throw new InvalidOperationException( $"{type.FullName} needs a parameterless constructor (it can be private) to be read from YAML." );

        return constructor.Invoke( BindingFlags.DoNotWrapExceptions, null, null, null );
    }

    InvalidOperationException Unsupported ( string reason ) =>
        new( $"{type.FullName} can't be converted to YAML: {reason}" );

    [UnconditionalSuppressMessage( "Trimming", "IL2062", Justification = TRIM_JUSTIFICATION )]
    [UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = TRIM_JUSTIFICATION )]
    YamlTypeKind DetermineKind ( [DynamicallyAccessedMembers( MEMBERS )] Type type )
    {
        if ( type.GetCustomAttribute< YamlConverterAttribute >() is { } converterAttribute )
        {
            converter = CreateConverter( converterAttribute.converterType );
            return YamlTypeKind.Converter;
        }

        if ( typeof( YamlNode ).IsAssignableFrom( type ) )
            return YamlTypeKind.Node;

        if ( type == typeof( object ) )
            return YamlTypeKind.Any;

        if ( type.IsEnum )
            return type.IsDefined( typeof( FlagsAttribute ), false ) ? YamlTypeKind.Flags : YamlTypeKind.Enum;

        switch ( Type.GetTypeCode( type ) )
        {
            case TypeCode.String: return YamlTypeKind.String;
            case TypeCode.Char: return YamlTypeKind.Char;
            case TypeCode.Boolean: return YamlTypeKind.Bool;
            case TypeCode.SByte: return Integer( sbyte.MinValue, sbyte.MaxValue );
            case TypeCode.Byte: return Integer( byte.MinValue, byte.MaxValue );
            case TypeCode.Int16: return Integer( short.MinValue, short.MaxValue );
            case TypeCode.UInt16: return Integer( ushort.MinValue, ushort.MaxValue );
            case TypeCode.Int32: return Integer( int.MinValue, int.MaxValue );
            case TypeCode.UInt32: return Integer( uint.MinValue, uint.MaxValue );
            case TypeCode.Int64: return Integer( long.MinValue, long.MaxValue );
            case TypeCode.UInt64: return Integer( ulong.MinValue, ulong.MaxValue );
            case TypeCode.Single: return YamlTypeKind.Single;
            case TypeCode.Double: return YamlTypeKind.Double;
            case TypeCode.Decimal: return YamlTypeKind.Decimal;
            case TypeCode.DateTime: throw Unsupported( "DateTime isn't supported; store a string or a number." );
        }

        if ( Nullable.GetUnderlyingType( type ) is { } underlying )
        {
            SetElement( underlying );
            return YamlTypeKind.Nullable;
        }

        if ( type.IsArray )
        {
            if ( type.GetArrayRank() != 1 )
                throw Unsupported( "only single-dimensional arrays are supported." );

            SetElement( type.GetElementType()! );
            return YamlTypeKind.Array;
        }

        if ( type.IsGenericType && !type.ContainsGenericParameters )
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if ( definition == typeof( List<> ) )
            {
                SetElement( arguments[0] );
                return YamlTypeKind.List;
            }

            if ( definition == typeof( HashSet<> ) )
            {
                SetElement( arguments[0] );
                setAdd = type.GetMethod( nameof( HashSet< int >.Add ) );
                return YamlTypeKind.Set;
            }

            if ( definition == typeof( Dictionary<,> ) )
            {
                keyType = arguments[0];
                var keyKind = Get( keyType ).kind;
                if ( keyKind is not ( YamlTypeKind.String or YamlTypeKind.Char or YamlTypeKind.Bool or YamlTypeKind.Integer or YamlTypeKind.Enum ) )
                    throw Unsupported( $"dictionary keys must be strings, numbers, booleans, characters or enums, not {keyType.Name}." );

                SetElement( arguments[1] );
                return YamlTypeKind.Dictionary;
            }
        }

        if ( typeof( Delegate ).IsAssignableFrom( type ) || type.IsPointer || type.IsByRef || type.ContainsGenericParameters )
            throw Unsupported( "delegates, pointers and open generic types have no YAML form." );

        if ( typeof( IEnumerable ).IsAssignableFrom( type ) )
            throw Unsupported( "collections must be T[], List<T>, HashSet<T> or Dictionary<TKey, TValue>." );

        if ( ( type.IsInterface || type.IsAbstract ) && YamlPolymorphism.Find( type ) == null )
            throw Unsupported( "abstract classes and interfaces need [YamlPolymorphic] on them or a base class so YAML can choose a concrete type." );

        return YamlTypeKind.Object;
    }

    YamlTypeKind Integer ( Int128 min, Int128 max )
    {
        this.min = min;
        this.max = max;
        return YamlTypeKind.Integer;
    }

    void SetElement ( [DynamicallyAccessedMembers( MEMBERS )] Type elementType )
    {
        this.elementType = elementType;
        Get( elementType );
        elementDefault = DefaultOf( elementType );
    }

    /// <summary>A boxed zero value for non-nullable value types; null otherwise.</summary>
    public static object? DefaultOf ( [DynamicallyAccessedMembers( MEMBERS )] Type type ) =>
        type.IsValueType && Nullable.GetUnderlyingType( type ) == null ? RuntimeHelpers.GetUninitializedObject( type ) : null;

    public static IYamlConverter CreateConverter ( [DynamicallyAccessedMembers( MEMBERS )] Type converterType )
    {
        if ( Converters.TryGetValue( converterType, out var existing ) )
            return existing;

        if ( !typeof( IYamlConverter ).IsAssignableFrom( converterType ) )
            throw new InvalidOperationException( $"{converterType.FullName} is used as a YAML converter but doesn't implement IYamlConverter." );

        var converter = ( IYamlConverter )Activator.CreateInstance( converterType )!;
        return Converters.GetOrAdd( converterType, converter );
    }

    // Members in declaration order, base classes first: fields, then properties, for each class in the hierarchy.
    [UnconditionalSuppressMessage( "Trimming", "IL2075", Justification = TRIM_JUSTIFICATION )]
    YamlMemberInfo[] BuildMembers ()
    {
        var hierarchy = new List< Type >();
        for ( var current = type; current != null && current != typeof( object ) && current != typeof( ValueType ); current = current.BaseType )
            hierarchy.Add( current );

        hierarchy.Reverse();

        var result = new List< YamlMemberInfo >();
        var byName = new Dictionary< string, YamlMemberInfo >( StringComparer.Ordinal );
        foreach ( var declaringType in hierarchy )
        {
            // Reflection returns members in declaration order (NativeAOT has no metadata tokens to sort by).
            var declared = declaringType.GetFields( DECLARED_MEMBERS ).Cast< MemberInfo >().Concat( declaringType.GetProperties( DECLARED_MEMBERS ) );

            foreach ( var member in declared )
            {
                var info = YamlMemberInfo.TryCreate( member );
                if ( info == null )
                    continue;

                if ( byName.TryGetValue( info.name, out var existing ) )
                {
                    throw new InvalidOperationException(
                        $"{type.FullName}: members '{existing.member.Name}' and '{member.Name}' both use the YAML key '{info.name}'." );
                }

                byName.Add( info.name, info );
                result.Add( info );
            }
        }

        if ( polymorphism is { } polymorphic && byName.ContainsKey( polymorphic.key ) )
            throw new InvalidOperationException( $"{type.FullName} has a member using the YAML key '{polymorphic.key}', which is its type discriminator." );

        return result.ToArray();
    }
}

sealed class YamlMemberInfo
{
    readonly FieldInfo? field;
    readonly PropertyInfo? property;

    YamlMemberInfo ( MemberInfo member, string name, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type )
    {
        this.member = member;
        this.name = name;
        this.type = type;
        field = member as FieldInfo;
        property = member as PropertyInfo;
        required = member.IsDefined( typeof( YamlRequiredAttribute ), true );

        var ignore = member.GetCustomAttribute< YamlIgnoreAttribute >( true );
        writeCondition = ignore?.condition;
        defaultValue = YamlTypeInfo.DefaultOf( type );

        try
        {
            if ( member.GetCustomAttribute< YamlConverterAttribute >( true ) is { } converterAttribute )
                converter = YamlTypeInfo.CreateConverter( converterAttribute.converterType );
            else
                YamlTypeInfo.Get( type );
        }
        catch ( InvalidOperationException exception )
        {
            throw new InvalidOperationException( $"{member.DeclaringType?.FullName}.{member.Name}: {exception.Message}", exception );
        }
    }

    public MemberInfo member { get; }

    /// <summary>The YAML key.</summary>
    public string name { get; }

    [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )]
    public Type type { get; }

    public bool required { get; }

    /// <summary>Null to always write; otherwise <see cref="YamlIgnoreCondition.WhenNull"/> or <see cref="YamlIgnoreCondition.WhenDefault"/>.</summary>
    public YamlIgnoreCondition? writeCondition { get; }

    public object? defaultValue { get; }

    public IYamlConverter? converter { get; }

    /// <summary>Returns null for members YAML leaves out; throws for members marked [YamlMember] that can't be used.</summary>
    [UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = "Member types are kept by rooting the game and engine assemblies." )]
    public static YamlMemberInfo? TryCreate ( MemberInfo member )
    {
        if ( member.IsDefined( typeof( CompilerGeneratedAttribute ), false ) )
            return null;

        var ignore = member.GetCustomAttribute< YamlIgnoreAttribute >( true );
        if ( ignore?.condition == YamlIgnoreCondition.Always )
            return null;

        var explicitMember = member.GetCustomAttribute< YamlMemberAttribute >( true );
        string name = explicitMember?.name ?? member.Name;

        if ( member is FieldInfo field )
        {
            if ( field.IsStatic || !field.IsPublic && explicitMember == null )
                return null;

            if ( field.IsInitOnly || field.IsLiteral )
                return explicitMember == null ? null : throw Invalid( member, "readonly fields can't be read from YAML." );

            return new YamlMemberInfo( member, name, field.FieldType );
        }

        if ( member is not PropertyInfo property || property.GetIndexParameters().Length > 0 )
            return null;

        var getter = property.GetMethod;
        var setter = property.SetMethod;
        var accessor = getter ?? setter!;
        if ( accessor.IsStatic )
            return null;

        bool isOverride = accessor.IsVirtual && accessor.GetBaseDefinition().DeclaringType != accessor.DeclaringType;
        if ( isOverride )
            return null;

        if ( explicitMember == null && !( getter is { IsPublic: true } && setter is { IsPublic: true } ) )
            return null;

        if ( getter == null || setter == null )
            throw Invalid( member, "[YamlMember] properties need both a getter and a setter." );

        return new YamlMemberInfo( member, name, property.PropertyType );
    }

    static InvalidOperationException Invalid ( MemberInfo member, string reason ) =>
        new( $"{member.DeclaringType?.FullName}.{member.Name}: {reason}" );

    public object? GetValue ( object target ) =>
        field != null ? field.GetValue( target ) : property!.GetValue( target, BindingFlags.DoNotWrapExceptions, null, null, null );

    public void SetValue ( object target, object? value )
    {
        if ( field != null )
            field.SetValue( target, value );
        else
            property!.SetValue( target, value, BindingFlags.DoNotWrapExceptions, null, null, null );
    }

    public bool ShouldWrite ( object? value ) => writeCondition switch
    {
        YamlIgnoreCondition.WhenNull => value != null,
        YamlIgnoreCondition.WhenDefault => !Equals( value, defaultValue ),
        _ => true,
    };
}

/// <summary>The concrete types under a <see cref="YamlPolymorphicAttribute"/> root, by discriminator name.</summary>
sealed class YamlPolymorphism
{
    static readonly ConcurrentDictionary< Type, YamlPolymorphism > Roots = new();

    readonly Dictionary< string, Type > typesByName = new( StringComparer.Ordinal );
    readonly Dictionary< Type, string > namesByType = new();

    public string key { get; }

    public Type root { get; }

    [UnconditionalSuppressMessage( "Trimming", "IL2026", Justification = "Polymorphic types live in the rooted game and engine assemblies." )]
    YamlPolymorphism ( Type root, YamlPolymorphicAttribute attribute )
    {
        this.root = root;
        key = attribute.discriminatorKey;

        Type?[] assemblyTypes;
        try
        {
            assemblyTypes = root.Assembly.GetTypes();
        }
        catch ( ReflectionTypeLoadException exception )
        {
            assemblyTypes = exception.Types;
        }

        var listed = root.GetCustomAttributes< YamlDerivedTypeAttribute >( false ).Select( derived => derived.derivedType );
        foreach ( var type in assemblyTypes.Concat( listed ) )
        {
            if ( type == null || type.IsAbstract || type.IsInterface || type.ContainsGenericParameters || !root.IsAssignableFrom( type ) )
                continue;

            string name = type.GetCustomAttribute< YamlTypeNameAttribute >( false )?.name ?? type.Name;
            if ( typesByName.TryGetValue( name, out var existing ) && existing != type )
                throw new InvalidOperationException( $"{existing.FullName} and {type.FullName} both use the YAML type name '{name}' under {root.FullName}." );

            typesByName[name] = type;
            namesByType[type] = name;
        }
    }

    /// <summary>The polymorphic root for a type: the type itself or its nearest base class with the attribute.</summary>
    public static YamlPolymorphism? Find ( Type type )
    {
        for ( var current = type; current != null; current = current.BaseType )
        {
            if ( current.GetCustomAttribute< YamlPolymorphicAttribute >( false ) is { } attribute )
                return Roots.GetOrAdd( current, root => new YamlPolymorphism( root, attribute ) );
        }

        if ( type.IsInterface && type.GetCustomAttribute< YamlPolymorphicAttribute >( false ) is { } interfaceAttribute )
            return Roots.GetOrAdd( type, root => new YamlPolymorphism( root, interfaceAttribute ) );

        return null;
    }

    public bool TryGetType ( string name, Type assignableTo, [NotNullWhen( true )] out Type? type )
    {
        if ( typesByName.TryGetValue( name, out type ) && assignableTo.IsAssignableFrom( type ) )
            return true;

        type = null;
        return false;
    }

    public bool TryGetName ( Type type, [NotNullWhen( true )] out string? name ) => namesByType.TryGetValue( type, out name );

    public string NamesAssignableTo ( Type type ) =>
        string.Join( ", ", typesByName.Where( entry => type.IsAssignableFrom( entry.Value ) ).Select( entry => entry.Key ).Order( StringComparer.OrdinalIgnoreCase ) );
}
