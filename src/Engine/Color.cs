namespace Engine;

public readonly record struct Color ( float R, float G, float B, float A = 1f )
{
    public static readonly Color White = new( 1f, 1f, 1f );
    public static readonly Color Black = new( 0f, 0f, 0f );

    public Color WithAlpha ( float a ) => this with { A = a };

    /// <param name="hue">0..1, wraps.</param>
    public static Color FromHsv ( float hue, float saturation, float value, float alpha = 1f )
    {
        float h = ( hue - MathF.Floor( hue ) ) * 6f;
        float c = value * saturation;
        float x = c * ( 1f - MathF.Abs( h % 2f - 1f ) );
        float m = value - c;
        var ( r, g, b ) = ( int )h switch
        {
            0 => ( c, x, 0f ),
            1 => ( x, c, 0f ),
            2 => ( 0f, c, x ),
            3 => ( 0f, x, c ),
            4 => ( x, 0f, c ),
            _ => ( c, 0f, x ),
        };
        return new Color( r + m, g + m, b + m, alpha );
    }
}
