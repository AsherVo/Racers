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
}