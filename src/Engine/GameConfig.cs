namespace Engine;

/// <summary>Window and resolution settings, read from the game's <c>config.yaml</c> before the window opens.</summary>
public sealed class GameConfig
{
    public string title = "Game";

    /// <summary>
    /// The game's size in game pixels. (0, 0) is the bottom-left corner and (resolutionX, resolutionY) the
    /// top-right. With <see cref="dynamicSize"/>, this is only the starting size.
    /// </summary>
    public int resolutionX = 1280;
    public int resolutionY = 720;

    /// <summary>Scales only by whole numbers (1x, 2x, 3x...), with black bars around the game.</summary>
    public bool pixelPerfect;

    /// <summary>The resolution follows the window's size instead of being fixed.</summary>
    public bool dynamicSize;

    /// <summary>With <see cref="dynamicSize"/>, how many window points one game pixel covers.</summary>
    public float dynamicPixelScale = 1f;

    /// <exception cref="InvalidDataException">A setting is out of range.</exception>
    public void Validate ( string source )
    {
        if ( resolutionX <= 0 || resolutionY <= 0 )
            throw new InvalidDataException( $"{source}: resolution must be positive, got {resolutionX}x{resolutionY}." );
        if ( !( dynamicPixelScale > 0f ) || float.IsInfinity( dynamicPixelScale ) )
            throw new InvalidDataException( $"{source}: dynamicPixelScale must be positive, got {dynamicPixelScale}." );
    }
}
