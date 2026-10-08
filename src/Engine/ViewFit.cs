using Engine.Native;

namespace Engine;

/// <summary>How a <see cref="GameConfig"/> maps the game onto the window: its resolution and SDL presentation mode.</summary>
internal readonly record struct ViewFit ( int Width, int Height, int Presentation )
{
    /// <summary>Largest share of the screen a new desktop window takes.</summary>
    const float MAX_SCREEN_FILL = 0.9f;

    /// <summary>Absorbs float error so an exact fit isn't floored one pixel short.</summary>
    const float EPSILON = 1e-3f;

    /// <param name="outputWidth">Window size in output pixels.</param>
    /// <param name="pixelDensity">Output pixels per window point.</param>
    public static ViewFit Compute ( GameConfig config, int outputWidth, int outputHeight, float pixelDensity )
    {
        int presentation = config.pixelPerfect ? SDL.LOGICAL_PRESENTATION_INTEGER_SCALE : SDL.LOGICAL_PRESENTATION_LETTERBOX;
        if ( !config.dynamicSize )
            return new ViewFit( config.resolutionX, config.resolutionY, presentation );

        float pixelSize = config.dynamicPixelScale * pixelDensity;
        if ( config.pixelPerfect )
            pixelSize = MathF.Max( 1f, MathF.Round( pixelSize ) );

        int width = Math.Max( 1, ( int )( outputWidth / pixelSize + EPSILON ) );
        int height = Math.Max( 1, ( int )( outputHeight / pixelSize + EPSILON ) );
        return new ViewFit( width, height, presentation );
    }

    /// <summary>
    /// The starting desktop window size, in points. A fixed resolution opens at the largest whole multiple that
    /// fits the screen; a dynamic one at its resolution times <see cref="GameConfig.dynamicPixelScale"/>, shrunk to
    /// fit if needed.
    /// </summary>
    /// <param name="usableWidth">Usable screen size in points, or 0 if unknown.</param>
    public static ( int Width, int Height ) InitialWindowSize ( GameConfig config, int usableWidth, int usableHeight )
    {
        float width = config.resolutionX, height = config.resolutionY;
        if ( config.dynamicSize )
        {
            width *= config.dynamicPixelScale;
            height *= config.dynamicPixelScale;
        }

        if ( usableWidth <= 0 || usableHeight <= 0 )
            return ( Math.Max( 1, ( int )width ), Math.Max( 1, ( int )height ) );

        float room = MathF.Min( usableWidth * MAX_SCREEN_FILL / width, usableHeight * MAX_SCREEN_FILL / height );
        float scale = config.dynamicSize ? MathF.Min( 1f, room ) : MathF.Max( 1f, MathF.Floor( room ) );
        return ( Math.Max( 1, ( int )( width * scale ) ), Math.Max( 1, ( int )( height * scale ) ) );
    }
}
