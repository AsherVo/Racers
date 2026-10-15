namespace Engine.Tests;

public class VectorTests
{
    [Fact]
    public void Vector2ArithmeticIsComponentWise ()
    {
        var a = new Vector2( 1f, 2f );
        var b = new Vector2( 3f, -4f );

        Assert.Equal( new Vector2( 4f, -2f ), a + b );
        Assert.Equal( new Vector2( -2f, 6f ), a - b );
        Assert.Equal( new Vector2( -1f, -2f ), -a );
        Assert.Equal( new Vector2( 3f, -8f ), a * b );
        Assert.Equal( new Vector2( 2f, 4f ), a * 2f );
        Assert.Equal( new Vector2( 2f, 4f ), 2f * a );
        Assert.Equal( new Vector2( 3f, -2f ), b / new Vector2( 1f, 2f ) );
        Assert.Equal( new Vector2( 0.5f, 1f ), a / 2f );
    }

    [Fact]
    public void Vector2CompoundAssignmentMutatesFields ()
    {
        var v = Vector2.One;
        v += Vector2.UnitX;
        v *= 3f;
        v.y -= 1f;

        Assert.Equal( 6f, v.x );
        Assert.Equal( 2f, v.y );
    }

    [Fact]
    public void Vector2Geometry ()
    {
        var v = new Vector2( 3f, 4f );

        Assert.Equal( 5f, v.length );
        Assert.Equal( 25f, v.lengthSquared );
        Assert.Equal( new Vector2( 0.6f, 0.8f ), v.normalized );
        Assert.Equal( Vector2.Zero, Vector2.Zero.normalized );
        Assert.Equal( 11f, Vector2.Dot( v, new Vector2( 1f, 2f ) ) );
        Assert.Equal( 1f, Vector2.Cross( Vector2.UnitX, Vector2.UnitY ) );
        Assert.Equal( 5f, Vector2.Distance( Vector2.Zero, v ) );
        Assert.Equal( new Vector2( 1.5f, 2f ), Vector2.Lerp( Vector2.Zero, v, 0.5f ) );

        var rotated = Vector2.Rotate( Vector2.UnitX, MathF.PI / 2f );
        Assert.Equal( 0f, rotated.x, 1e-6f );
        Assert.Equal( 1f, rotated.y, 1e-6f );
    }

    [Fact]
    public void Vector2MinMaxClampAbs ()
    {
        var a = new Vector2( -1f, 5f );
        var b = new Vector2( 2f, 3f );

        Assert.Equal( new Vector2( -1f, 3f ), Vector2.Min( a, b ) );
        Assert.Equal( new Vector2( 2f, 5f ), Vector2.Max( a, b ) );
        Assert.Equal( new Vector2( 0f, 4f ), Vector2.Clamp( a, Vector2.Zero, new Vector2( 4f ) ) );
        Assert.Equal( new Vector2( 1f, 5f ), Vector2.Abs( a ) );
    }

    [Fact]
    public void Vector2EqualityMatchesFloatSemantics ()
    {
        var a = new Vector2( float.NaN, 0f );
        var b = new Vector2( float.NaN, 0f );

        Assert.False( a == b );
        Assert.True( a.Equals( b ) );
        Assert.Equal( new Vector2( 1f, 2f ).GetHashCode(), new Vector2( 1f, 2f ).GetHashCode() );
        Assert.Equal( "(1.5, -2)", new Vector2( 1.5f, -2f ).ToString() );
    }

    [Fact]
    public void Vector2IntArithmeticIsComponentWise ()
    {
        var a = new Vector2Int( 7, -7 );
        var b = new Vector2Int( 2, 3 );

        Assert.Equal( new Vector2Int( 9, -4 ), a + b );
        Assert.Equal( new Vector2Int( 5, -10 ), a - b );
        Assert.Equal( new Vector2Int( -7, 7 ), -a );
        Assert.Equal( new Vector2Int( 14, -21 ), a * b );
        Assert.Equal( new Vector2Int( 14, -14 ), a * 2 );
        Assert.Equal( new Vector2Int( 14, -14 ), 2 * a );
        Assert.Equal( new Vector2Int( 3, -2 ), a / b );
        Assert.Equal( new Vector2Int( 3, -3 ), a / 2 );
        Assert.Equal( new Vector2Int( 1, -1 ), a % b );
        Assert.Equal( new Vector2Int( 1, -1 ), a % 2 );
    }

    [Fact]
    public void Vector2IntGeometry ()
    {
        var v = new Vector2Int( 3, 4 );

        Assert.Equal( 5f, v.length );
        Assert.Equal( 25, v.lengthSquared );
        Assert.Equal( 11, Vector2Int.Dot( v, new Vector2Int( 1, 2 ) ) );
        Assert.Equal( 7, Vector2Int.ManhattanDistance( Vector2Int.Zero, v ) );
        Assert.Equal( new Vector2Int( 3, 0 ), Vector2Int.Clamp( new Vector2Int( 5, -1 ), Vector2Int.Zero, v ) );
    }

    [Fact]
    public void Vector2IntConvertsToAndFromVector2 ()
    {
        Vector2 widened = new Vector2Int( 2, -3 );
        var f = new Vector2( 1.5f, -1.5f );

        Assert.Equal( new Vector2( 2f, -3f ), widened );
        Assert.Equal( new Vector2Int( 1, -1 ), ( Vector2Int )f );
        Assert.Equal( new Vector2Int( 1, -2 ), Vector2Int.Floor( f ) );
        Assert.Equal( new Vector2Int( 2, -1 ), Vector2Int.Ceiling( f ) );
        Assert.Equal( new Vector2Int( 2, -2 ), Vector2Int.Round( f ) );
    }

    [Fact]
    public void MixingIntAndFloatVectorsProducesVector2 ()
    {
        var grid = new Vector2Int( 2, 3 );

        Assert.Equal( new Vector2( 2.5f, 3.5f ), grid + new Vector2( 0.5f ) );
        Assert.Equal( new Vector2( 1f, 1.5f ), grid * 0.5f );
        Assert.Equal( new Vector2( 1f, 1.5f ), 0.5f * grid );
        Assert.Equal( new Vector2( 1f, 1.5f ), grid / 2f );
        Assert.Equal( new Vector2Int( 1, 1 ), grid / 2 );
    }

    [Fact]
    public void VectorsRoundTripThroughYaml ()
    {
        Assert.Equal( new Vector2( 1.5f, -2f ), Yaml.Deserialize< Vector2 >( Yaml.Serialize( new Vector2( 1.5f, -2f ) ) ) );
        Assert.Equal( new Vector2Int( 4, -5 ), Yaml.Deserialize< Vector2Int >( Yaml.Serialize( new Vector2Int( 4, -5 ) ) ) );
    }
}
