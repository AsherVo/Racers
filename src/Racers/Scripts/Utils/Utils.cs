using Engine;
using System;
using System.Reflection;
using System.Text;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Vo;

public static class Utils
{
	public static string ObjectToString ( object obj )
    {
        if ( obj == null )
            return "[null object]";

        var sb = new StringBuilder();
        sb.Append( "[" );

        Type type = obj.GetType();
        sb.Append( type.Name );
        sb.Append( "]" );

        // Print fields
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (FieldInfo field in fields)
        {
            object? value = field.GetValue( obj );
            sb.AppendLine($"  {field.Name} ({field.FieldType.Name}): {value}");
        }

        // Print properties
        PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (PropertyInfo property in properties)
        {
            if (property.CanRead)
            {
                object? value = property.GetValue(obj);
                sb.AppendLine($"  {property.Name} ({property.PropertyType.Name}): {value}");
            }
        }

        return sb.ToString();
    }

    public static float Lerp ( float a, float b, float t )
        => a + ( b - a ) * t;

    public static float Clamp ( float value, float min, float max )
        => MathF.Min( MathF.Max( value, min ), max );

    public static float Clamp01 ( float value )
        => Clamp( value, 0f, 1f );

    public static string ColorToHex ( Color color, bool includeAlpha = false )
    {
        string hex = ChannelToHex( color.r ) + ChannelToHex( color.g ) + ChannelToHex( color.b );
        return includeAlpha ? hex + ChannelToHex( color.a ) : hex;
    }

    public static Color HexToColor ( string color )
    {
        float r = HexToChannel( color, 0 );
        float g = HexToChannel( color, 2 );
        float b = HexToChannel( color, 4 );
        float a = color.Length >= 8 ? HexToChannel( color, 6 ) : 1f;
        return new Color( r, g, b, a );
    }

    static string ChannelToHex ( float channel ) => ( ( int )MathF.Round( Math.Clamp( channel, 0f, 1f ) * 255f ) ).ToString( "X2" );

    static float HexToChannel ( string hex, int start ) => int.Parse( hex.AsSpan( start, 2 ), NumberStyles.HexNumber ) / 255f;
}