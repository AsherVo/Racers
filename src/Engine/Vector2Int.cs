using System.Globalization;

namespace Engine;

public struct Vector2Int : IEquatable< Vector2Int >
{
    public int x;
    public int y;

    public static Vector2Int Zero => new( 0, 0 );
    public static Vector2Int One => new( 1, 1 );
    public static Vector2Int UnitX => new( 1, 0 );
    public static Vector2Int UnitY => new( 0, 1 );

    public Vector2Int ( int x, int y )
    {
        this.x = x;
        this.y = y;
    }

    public Vector2Int ( int value ) : this( value, value ) { }

    public readonly float length => MathF.Sqrt( lengthSquared );
    public readonly int lengthSquared => x * x + y * y;

    public readonly void Deconstruct ( out int x, out int y )
    {
        x = this.x;
        y = this.y;
    }

    public static int Dot ( Vector2Int a, Vector2Int b ) => a.x * b.x + a.y * b.y;

    /// <summary>Z of the 3D cross product: positive when <paramref name="b"/> is counter-clockwise of <paramref name="a"/>.</summary>
    public static int Cross ( Vector2Int a, Vector2Int b ) => a.x * b.y - a.y * b.x;

    public static float Distance ( Vector2Int a, Vector2Int b ) => ( a - b ).length;
    public static int DistanceSquared ( Vector2Int a, Vector2Int b ) => ( a - b ).lengthSquared;
    public static int ManhattanDistance ( Vector2Int a, Vector2Int b ) => Math.Abs( a.x - b.x ) + Math.Abs( a.y - b.y );

    public static Vector2Int Min ( Vector2Int a, Vector2Int b ) => new( Math.Min( a.x, b.x ), Math.Min( a.y, b.y ) );
    public static Vector2Int Max ( Vector2Int a, Vector2Int b ) => new( Math.Max( a.x, b.x ), Math.Max( a.y, b.y ) );
    public static Vector2Int Clamp ( Vector2Int value, Vector2Int min, Vector2Int max ) => Min( Max( value, min ), max );
    public static Vector2Int Abs ( Vector2Int value ) => new( Math.Abs( value.x ), Math.Abs( value.y ) );

    public static Vector2Int Floor ( Vector2 value ) => new( ( int )MathF.Floor( value.x ), ( int )MathF.Floor( value.y ) );
    public static Vector2Int Ceiling ( Vector2 value ) => new( ( int )MathF.Ceiling( value.x ), ( int )MathF.Ceiling( value.y ) );

    /// <summary>Midpoints round away from zero, so 0.5 becomes 1 and -0.5 becomes -1.</summary>
    public static Vector2Int Round ( Vector2 value ) =>
        new( ( int )MathF.Round( value.x, MidpointRounding.AwayFromZero ), ( int )MathF.Round( value.y, MidpointRounding.AwayFromZero ) );

    public static implicit operator Vector2 ( Vector2Int value ) => new( value.x, value.y );

    /// <summary>Truncates toward zero; use <see cref="Floor"/>, <see cref="Ceiling"/> or <see cref="Round"/> otherwise.</summary>
    public static explicit operator Vector2Int ( Vector2 value ) => new( ( int )value.x, ( int )value.y );

    public static Vector2Int operator + ( Vector2Int a, Vector2Int b ) => new( a.x + b.x, a.y + b.y );
    public static Vector2Int operator - ( Vector2Int a, Vector2Int b ) => new( a.x - b.x, a.y - b.y );
    public static Vector2Int operator - ( Vector2Int value ) => new( -value.x, -value.y );
    public static Vector2Int operator * ( Vector2Int a, Vector2Int b ) => new( a.x * b.x, a.y * b.y );
    public static Vector2Int operator * ( Vector2Int value, int scalar ) => new( value.x * scalar, value.y * scalar );
    public static Vector2Int operator * ( int scalar, Vector2Int value ) => new( value.x * scalar, value.y * scalar );

    /// <summary>Integer division: truncates toward zero.</summary>
    public static Vector2Int operator / ( Vector2Int a, Vector2Int b ) => new( a.x / b.x, a.y / b.y );

    /// <summary>Integer division: truncates toward zero.</summary>
    public static Vector2Int operator / ( Vector2Int value, int scalar ) => new( value.x / scalar, value.y / scalar );

    public static Vector2 operator * ( Vector2Int value, float scalar ) => new( value.x * scalar, value.y * scalar );
    public static Vector2 operator * ( float scalar, Vector2Int value ) => new( value.x * scalar, value.y * scalar );
    public static Vector2 operator / ( Vector2Int value, float scalar ) => new( value.x / scalar, value.y / scalar );

    public static Vector2Int operator % ( Vector2Int a, Vector2Int b ) => new( a.x % b.x, a.y % b.y );
    public static Vector2Int operator % ( Vector2Int value, int scalar ) => new( value.x % scalar, value.y % scalar );

    public static bool operator == ( Vector2Int a, Vector2Int b ) => a.x == b.x && a.y == b.y;
    public static bool operator != ( Vector2Int a, Vector2Int b ) => !( a == b );

    public readonly bool Equals ( Vector2Int other ) => this == other;
    public override readonly bool Equals ( object? obj ) => obj is Vector2Int other && Equals( other );
    public override readonly int GetHashCode () => HashCode.Combine( x, y );
    public override readonly string ToString () => string.Create( CultureInfo.InvariantCulture, $"({x}, {y})" );
}
