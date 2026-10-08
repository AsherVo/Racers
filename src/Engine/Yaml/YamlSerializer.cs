using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;

namespace Engine;

/// <summary>Reflection-based conversion between objects and YAML nodes, driven by <see cref="YamlTypeInfo"/>.</summary>
static class YamlSerializer
{
    const string TRIM_JUSTIFICATION = "Types reached through member signatures are kept by rooting the game and engine assemblies.";

    public static object? Read ( YamlNode node, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type, YamlReadContext context )
    {
        var info = YamlTypeInfo.Get( type );
        if ( node.isNull )
        {
            if ( type.IsValueType && info.kind != YamlTypeKind.Nullable )
                context.Expected( node, info.description );

            return null;
        }

        switch ( info.kind )
        {
            case YamlTypeKind.String:
                return context.ReadString( node );
            case YamlTypeKind.Char:
                return context.ReadChar( node );
            case YamlTypeKind.Bool:
                return context.ReadBool( node );
            case YamlTypeKind.Integer:
                return ToInteger( context.ReadInteger( node, info.min, info.max ), type );
            case YamlTypeKind.Single:
                return context.ReadSingle( node );
            case YamlTypeKind.Double:
                return context.ReadDouble( node );
            case YamlTypeKind.Decimal:
                return context.ReadDecimal( node );
            case YamlTypeKind.Enum:
                return ReadEnum( node, type, context );
            case YamlTypeKind.Flags:
                return ReadFlags( node, type, context );
            case YamlTypeKind.Nullable:
                return Read( node, info.elementType!, context );
            case YamlTypeKind.Array or YamlTypeKind.List or YamlTypeKind.Set:
                return ReadCollection( node, info, context );
            case YamlTypeKind.Dictionary:
                return ReadDictionary( node, info, context );
            case YamlTypeKind.Node:
                if ( type.IsInstanceOfType( node ) )
                    return node;

                context.Expected( node, type == typeof( YamlMap ) ? "a map" : type == typeof( YamlList ) ? "a list" : "a scalar" );
                return null;
            case YamlTypeKind.Any:
                return ReadAny( node, context );
            case YamlTypeKind.Converter:
                return info.converter!.Read( node, type, context );
            default:
                return ReadObject( node, info, context );
        }
    }

    static object ToInteger ( Int128 value, Type type ) => Type.GetTypeCode( type ) switch
    {
        TypeCode.SByte => ( sbyte )value,
        TypeCode.Byte => ( byte )value,
        TypeCode.Int16 => ( short )value,
        TypeCode.UInt16 => ( ushort )value,
        TypeCode.Int32 => ( int )value,
        TypeCode.UInt32 => ( uint )value,
        TypeCode.Int64 => ( long )value,
        _ => ( ulong )value,
    };

    static bool TryParseEnumName ( Type type, string text, [NotNullWhen( true )] out object? value )
    {
        value = null;
        if ( text.Length == 0 || !( char.IsLetter( text[0] ) || text[0] == '_' ) || text.Contains( ',' ) )
            return false;

        return Enum.TryParse( type, text, ignoreCase: false, out value ) && Enum.IsDefined( type, value );
    }

    static object? ReadEnum ( YamlNode node, Type type, YamlReadContext context )
    {
        if ( !context.TryGetText( node, $"a {type.Name}", out string text ) )
            return null;

        if ( TryParseEnumName( type, text, out object? value ) )
            return value;

        context.AddError( node, $"'{text}' is not a {type.Name}. Expected one of: {string.Join( ", ", Enum.GetNames( type ) )}." );
        return null;
    }

    // [Flags] enums read from a list of names, comma-separated names, or a single name.
    static object? ReadFlags ( YamlNode node, Type type, YamlReadContext context )
    {
        var names = new List< ( YamlNode Node, string Name ) >();
        if ( node is YamlList list )
        {
            foreach ( var item in list )
            {
                if ( context.TryGetText( item, $"a {type.Name} name", out string itemText ) )
                    names.Add( ( item, itemText ) );
            }
        }
        else if ( context.TryGetText( node, $"{type.Name} names", out string text ) )
        {
            names.AddRange( text.Split( ',', StringSplitOptions.TrimEntries ).Select( name => ( node, name ) ) );
        }
        else
        {
            return null;
        }

        ulong bits = 0;
        foreach ( var ( nameNode, name ) in names )
        {
            if ( TryParseEnumName( type, name, out object? flag ) )
                bits |= ToBits( flag );
            else
                context.AddError( nameNode, $"'{name}' is not a {type.Name}. Expected any of: {string.Join( ", ", Enum.GetNames( type ) )}." );
        }

        return Enum.ToObject( type, bits );
    }

    // Works on boxed enums and boxed underlying integers alike, since both unbox to the underlying type.
    static ulong ToBits ( object value ) => Type.GetTypeCode( value.GetType() ) switch
    {
        TypeCode.SByte => ( byte )( sbyte )value,
        TypeCode.Byte => ( byte )value,
        TypeCode.Int16 => ( ushort )( short )value,
        TypeCode.UInt16 => ( ushort )value,
        TypeCode.Int32 => ( uint )( int )value,
        TypeCode.UInt32 => ( uint )value,
        TypeCode.Int64 => ( ulong )( long )value,
        _ => ( ulong )value,
    };

    [UnconditionalSuppressMessage( "AOT", "IL3050", Justification = "The array type comes from a member signature in a rooted assembly." )]
    static object? ReadCollection ( YamlNode node, YamlTypeInfo info, YamlReadContext context )
    {
        if ( node is not YamlList list )
        {
            context.Expected( node, "a list" );
            return null;
        }

        if ( !context.Enter( node ) )
            return null;

        var elementType = info.elementType!;
        var array = info.kind == YamlTypeKind.Array ? Array.CreateInstanceFromArrayType( info.type, list.Count ) : null;
        var collection = array == null ? info.CreateInstance() : null;
        for ( int i = 0; i < list.Count; i++ )
        {
            context.PushPath( i );
            var item = Read( list[i], elementType, context ) ?? info.elementDefault;
            if ( array != null )
            {
                array.SetValue( item, i );
            }
            else if ( info.kind == YamlTypeKind.List )
            {
                ( ( IList )collection! ).Add( item );
            }
            else if ( !( bool )info.setAdd!.Invoke( collection, BindingFlags.DoNotWrapExceptions, null, [item], null )! )
            {
                context.AddError( list[i], "Duplicate item in a set." );
            }

            context.PopPath();
        }

        context.Exit();
        return array ?? collection;
    }

    static object? ReadDictionary ( YamlNode node, YamlTypeInfo info, YamlReadContext context )
    {
        if ( node is not YamlMap map )
        {
            context.Expected( node, "a map" );
            return null;
        }

        if ( !context.Enter( node ) )
            return null;

        var dictionary = ( IDictionary )info.CreateInstance();
        foreach ( var ( keyNode, valueNode ) in map )
        {
            context.PushPath( keyNode is YamlScalar scalar ? scalar.value : "?" );
            int errorCount = context.errors.Count;
            var key = Read( keyNode, info.keyType!, context );
            var value = Read( valueNode, info.elementType!, context ) ?? info.elementDefault;
            if ( key == null )
            {
                if ( context.errors.Count == errorCount )
                    context.AddError( keyNode, "Dictionary keys can't be null." );
            }
            else if ( dictionary.Contains( key ) )
            {
                context.AddError( keyNode, $"Duplicate key '{key}'." );
            }
            else
            {
                dictionary.Add( key, value );
            }

            context.PopPath();
        }

        context.Exit();
        return dictionary;
    }

    /// <summary>Arbitrary data: null, bool, long, double or string scalars; List&lt;object?&gt;; Dictionary&lt;string, object?&gt;.</summary>
    static object? ReadAny ( YamlNode node, YamlReadContext context )
    {
        switch ( node )
        {
            case YamlScalar scalar:
                return scalar.Resolve();

            case YamlList list:
                {
                    if ( !context.Enter( node ) )
                        return null;

                    var result = new List< object? >( list.Count );
                    for ( int i = 0; i < list.Count; i++ )
                    {
                        context.PushPath( i );
                        result.Add( ReadAny( list[i], context ) );
                        context.PopPath();
                    }

                    context.Exit();
                    return result;
                }

            default:
                {
                    var map = ( YamlMap )node;
                    if ( !context.Enter( node ) )
                        return null;

                    var result = new Dictionary< string, object? >( map.Count, StringComparer.Ordinal );
                    foreach ( var ( keyNode, valueNode ) in map )
                    {
                        if ( keyNode is not YamlScalar key )
                        {
                            context.AddError( keyNode, "Only scalar keys can be read as a Dictionary<string, object>." );
                            continue;
                        }

                        context.PushPath( key.value );
                        result[key.value] = ReadAny( valueNode, context );
                        context.PopPath();
                    }

                    context.Exit();
                    return result;
                }
        }
    }

    [UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = TRIM_JUSTIFICATION )]
    static object? ReadObject ( YamlNode node, YamlTypeInfo info, YamlReadContext context )
    {
        if ( node is not YamlMap map )
        {
            context.Expected( node, $"a map for {info.type.Name}" );
            return null;
        }

        var consumed = new bool[map.Count];
        var concrete = info;
        if ( info.polymorphism is { } polymorphism )
        {
            var chosen = ChooseType( map, info.type, polymorphism, consumed, context );
            if ( chosen == null )
                return null;

            concrete = YamlTypeInfo.Get( chosen );
        }

        if ( !context.Enter( node ) )
            return null;

        var instance = concrete.CreateInstance();
        foreach ( var member in concrete.members )
        {
            int index = map.IndexOf( member.name );
            if ( index < 0 )
            {
                if ( member.required )
                    context.AddError( map, $"Missing required key '{member.name}' for {concrete.type.Name}." );

                continue;
            }

            consumed[index] = true;
            context.PushPath( member.name );
            var valueNode = map[index].Value;
            var value = member.converter == null || valueNode.isNull
                ? Read( valueNode, member.type, context )
                : member.converter.Read( valueNode, member.type, context );

            member.SetValue( instance, value );
            context.PopPath();
        }

        if ( !context.options.allowUnknownKeys )
            ReportUnknownKeys( map, consumed, concrete.type, context );

        context.Exit();
        return instance;
    }

    static Type? ChooseType ( YamlMap map, Type staticType, YamlPolymorphism polymorphism, bool[] consumed, YamlReadContext context )
    {
        int index = map.IndexOf( polymorphism.key );
        if ( index < 0 )
        {
            if ( !staticType.IsAbstract && !staticType.IsInterface )
                return staticType;

            context.AddError( map, $"Missing '{polymorphism.key}' to choose which {staticType.Name} to create. "
                + $"Expected one of: {polymorphism.NamesAssignableTo( staticType )}." );
            return null;
        }

        consumed[index] = true;
        var discriminator = map[index].Value;
        context.PushPath( polymorphism.key );
        Type? chosen = null;
        if ( discriminator is not YamlScalar { isNullValue: false } scalar )
            context.Expected( discriminator, $"a {staticType.Name} type name" );
        else if ( !polymorphism.TryGetType( scalar.value, staticType, out chosen ) )
            context.AddError( discriminator, $"Unknown {staticType.Name} type '{scalar.value}'. Expected one of: {polymorphism.NamesAssignableTo( staticType )}." );

        context.PopPath();
        return chosen;
    }

    static void ReportUnknownKeys ( YamlMap map, bool[] consumed, Type type, YamlReadContext context )
    {
        for ( int i = 0; i < consumed.Length; i++ )
        {
            if ( consumed[i] )
                continue;

            var keyNode = map[i].Key;
            string key = keyNode is YamlScalar scalar ? scalar.value : YamlReadContext.Describe( keyNode );
            context.PushPath( key );
            context.AddError( keyNode, $"Unknown key '{key}' for {type.Name}." );
            context.PopPath();
        }
    }

    [UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = TRIM_JUSTIFICATION )]
    public static YamlNode Write ( object? value, [DynamicallyAccessedMembers( YamlTypeInfo.MEMBERS )] Type type, YamlWriteContext context )
    {
        if ( value == null )
            return YamlScalar.Null();

        var info = YamlTypeInfo.Get( type );
        switch ( info.kind )
        {
            case YamlTypeKind.String:
                return new YamlScalar( ( string )value );
            case YamlTypeKind.Char:
                return new YamlScalar( value.ToString()! );
            case YamlTypeKind.Bool:
                return Plain( ( bool )value ? "true" : "false" );
            case YamlTypeKind.Integer:
                return Plain( ( ( IFormattable )value ).ToString( null, CultureInfo.InvariantCulture ) );
            case YamlTypeKind.Single:
                return Plain( FormatSingle( ( float )value ) );
            case YamlTypeKind.Double:
                return Plain( FormatDouble( ( double )value ) );
            case YamlTypeKind.Decimal:
                return Plain( ( ( decimal )value ).ToString( CultureInfo.InvariantCulture ) );
            case YamlTypeKind.Enum:
                return new YamlScalar( Enum.GetName( type, value )
                    ?? throw new InvalidOperationException( $"{value} is not a named {type.Name} value." ) );
            case YamlTypeKind.Flags:
                return WriteFlags( value, type );
            case YamlTypeKind.Nullable:
                return Write( value, info.elementType!, context );
            case YamlTypeKind.Array or YamlTypeKind.List or YamlTypeKind.Set:
                {
                    context.Enter();
                    var list = new YamlList();
                    foreach ( var item in ( IEnumerable )value )
                        list.Add( Write( item, info.elementType!, context ) );

                    context.Exit();
                    return list;
                }
            case YamlTypeKind.Dictionary:
                {
                    context.Enter();
                    var map = new YamlMap();
                    foreach ( DictionaryEntry entry in ( IDictionary )value )
                        map.Add( Write( entry.Key, info.keyType!, context ), Write( entry.Value, info.elementType!, context ) );

                    context.Exit();
                    return map;
                }
            case YamlTypeKind.Node:
                return ( YamlNode )value;
            case YamlTypeKind.Any:
                return value.GetType() == typeof( object ) ? new YamlMap() : Write( value, value.GetType(), context );
            case YamlTypeKind.Converter:
                return info.converter!.Write( value, type, context );
            default:
                return WriteObject( value, info, context );
        }
    }

    static YamlScalar Plain ( string text ) => new( text, YamlScalarStyle.Plain );

    static string FormatDouble ( double value ) => value switch
    {
        double.PositiveInfinity => ".inf",
        double.NegativeInfinity => "-.inf",
        _ when double.IsNaN( value ) => ".nan",
        _ => KeepFloatForm( value.ToString( "R", CultureInfo.InvariantCulture ) ),
    };

    static string FormatSingle ( float value ) =>
        float.IsFinite( value ) ? KeepFloatForm( value.ToString( "R", CultureInfo.InvariantCulture ) ) : FormatDouble( value );

    // "3" would read back as an integer into object members; "3.0" stays a float.
    static string KeepFloatForm ( string text ) => text.AsSpan().IndexOfAny( ".Ee" ) >= 0 ? text : text + ".0";

    // [Flags] values are written as comma-separated names, e.g. "Fire, Ice".
    static YamlNode WriteFlags ( object value, Type type )
    {
        if ( Enum.GetName( type, value ) is { } exact )
            return new YamlScalar( exact );

        ulong remaining = ToBits( value );
        var names = new List< string >();
        var flags = Enum.GetValuesAsUnderlyingType( type ).Cast< object >().OrderByDescending( ToBits );
        foreach ( var flag in flags )
        {
            ulong bits = ToBits( flag );
            if ( bits != 0 && ( remaining & bits ) == bits )
            {
                names.Add( Enum.GetName( type, flag )! );
                remaining &= ~bits;
            }
        }

        if ( remaining != 0 )
            throw new InvalidOperationException( $"{value} has bits that are not named {type.Name} flags." );

        names.Reverse();
        return names.Count == 0 ? new YamlList() : new YamlScalar( string.Join( ", ", names ) );
    }

    [UnconditionalSuppressMessage( "Trimming", "IL2072", Justification = TRIM_JUSTIFICATION )]
    static YamlNode WriteObject ( object value, YamlTypeInfo info, YamlWriteContext context )
    {
        var concrete = info;
        string? typeName = null;
        if ( info.polymorphism is { } polymorphism )
        {
            var runtimeType = value.GetType();
            if ( !polymorphism.TryGetName( runtimeType, out typeName ) )
            {
                throw new InvalidOperationException( $"{runtimeType.FullName} is not a YAML type under {polymorphism.root.FullName}. "
                    + "Subclasses outside the root's assembly need [YamlDerivedType] on the root." );
            }

            concrete = YamlTypeInfo.Get( runtimeType );
        }

        context.Enter();
        var map = new YamlMap();
        if ( typeName != null )
            map.Add( info.polymorphism!.key, new YamlScalar( typeName ) );

        foreach ( var member in concrete.members )
        {
            var memberValue = member.GetValue( value );
            if ( !member.ShouldWrite( memberValue ) )
                continue;

            var node = member.converter == null || memberValue == null
                ? Write( memberValue, member.type, context )
                : member.converter.Write( memberValue, member.type, context );

            map.Add( member.name, node );
        }

        context.Exit();
        return map;
    }
}
