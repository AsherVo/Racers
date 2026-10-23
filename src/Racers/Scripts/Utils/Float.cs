using System;

namespace Vo;

public static class Float
{
    public static float Lerp ( float a, float b, float t )
        => a + ( b - a ) * t;

    public static float Clamp ( float value, float min, float max )
        => MathF.Min( MathF.Max( value, min ), max );

    public static float Clamp01 ( float value )
        => Clamp( value, 0f, 1f );
}
