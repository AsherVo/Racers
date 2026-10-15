using System.Globalization;

namespace Engine;

public struct Vector2 : IEquatable< Vector2 >
{
    public float x;
    public float y;

    public static Vector2 Zero => new( 0f, 0f );
    public static Vector2 One => new( 1f, 1f );
    public static Vector2 UnitX => new( 1f, 0f );
    public static Vector2 UnitY => new( 0f, 1f );

    public Vector2 ( float x, float y )
    {
        this.x = x;
        this.y = y;
    }

    public Vector2 ( float value ) : this( value, value ) { }

    public readonly float length => MathF.Sqrt( lengthSquared );
    public readonly float lengthSquared => x * x + y * y;

    /// <summary>Unit-length copy, or <see cref="Zero"/> for a zero-length vector.</summary>
    public readonly Vector2 normalized
    {
        get
        {
            float len = length;
            return len > 0f ? this / len : Zero;
        }
    }

    public readonly void Deconstruct ( out float x, out float y )
    {
        x = this.x;
        y = this.y;
    }

    public static float Dot ( Vector2 a, Vector2 b ) => a.x * b.x + a.y * b.y;

    /// <summary>Z of the 3D cross product: positive when <paramref name="b"/> is counter-clockwise of <paramref name="a"/>.</summary>
    public static float Cross ( Vector2 a, Vector2 b ) => a.x * b.y - a.y * b.x;

    public static float Distance ( Vector2 a, Vector2 b ) => ( a - b ).length;
    public static float DistanceSquared ( Vector2 a, Vector2 b ) => ( a - b ).lengthSquared;

    /// <summary>Unclamped: <paramref name="t"/> outside 0..1 extrapolates.</summary>
    public static Vector2 Lerp ( Vector2 a, Vector2 b, float t ) => a + ( b - a ) * t;

    public static Vector2 Min ( Vector2 a, Vector2 b ) => new( MathF.Min( a.x, b.x ), MathF.Min( a.y, b.y ) );
    public static Vector2 Max ( Vector2 a, Vector2 b ) => new( MathF.Max( a.x, b.x ), MathF.Max( a.y, b.y ) );
    public static Vector2 Clamp ( Vector2 value, Vector2 min, Vector2 max ) => Min( Max( value, min ), max );
    public static Vector2 Abs ( Vector2 value ) => new( MathF.Abs( value.x ), MathF.Abs( value.y ) );

    /// <param name="radians">Counter-clockwise.</param>
    public static Vector2 Rotate ( Vector2 value, float radians )
    {
        var ( sin, cos ) = MathF.SinCos( radians );
        return new Vector2( value.x * cos - value.y * sin, value.x * sin + value.y * cos );
    }

    public static Vector2 operator + ( Vector2 a, Vector2 b ) => new( a.x + b.x, a.y + b.y );
    public static Vector2 operator - ( Vector2 a, Vector2 b ) => new( a.x - b.x, a.y - b.y );
    public static Vector2 operator - ( Vector2 value ) => new( -value.x, -value.y );
    public static Vector2 operator * ( Vector2 a, Vector2 b ) => new( a.x * b.x, a.y * b.y );
    public static Vector2 operator * ( Vector2 value, float scalar ) => new( value.x * scalar, value.y * scalar );
    public static Vector2 operator * ( float scalar, Vector2 value ) => new( value.x * scalar, value.y * scalar );
    public static Vector2 operator / ( Vector2 a, Vector2 b ) => new( a.x / b.x, a.y / b.y );
    public static Vector2 operator / ( Vector2 value, float scalar ) => new( value.x / scalar, value.y / scalar );

    public static bool operator == ( Vector2 a, Vector2 b ) => a.x == b.x && a.y == b.y;
    public static bool operator != ( Vector2 a, Vector2 b ) => !( a == b );

    public readonly bool Equals ( Vector2 other ) => x.Equals( other.x ) && y.Equals( other.y );
    public override readonly bool Equals ( object? obj ) => obj is Vector2 other && Equals( other );
    public override readonly int GetHashCode () => HashCode.Combine( x, y );
    public override readonly string ToString () => string.Create( CultureInfo.InvariantCulture, $"({x}, {y})" );
}
