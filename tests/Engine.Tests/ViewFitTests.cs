using Engine.Native;

namespace Engine.Tests;

public class ViewFitTests
{
    static GameConfig Config ( bool pixelPerfect = false, bool dynamicSize = false, float dynamicPixelScale = 4f ) => new()
    {
        resolutionX = 400,
        resolutionY = 225,
        pixelPerfect = pixelPerfect,
        dynamicSize = dynamicSize,
        dynamicPixelScale = dynamicPixelScale,
    };

    [Fact]
    public void ReadsConfigYaml ()
    {
        var config = Yaml.Deserialize< GameConfig >( """
            title: Resource Racers
            resolutionX: 400
            resolutionY: 225
            pixelPerfect: true
            dynamicSize: true
            dynamicPixelScale: 4
            """ );

        Assert.Equal( "Resource Racers", config.title );
        Assert.Equal( 400, config.resolutionX );
        Assert.Equal( 225, config.resolutionY );
        Assert.True( config.pixelPerfect );
        Assert.True( config.dynamicSize );
        Assert.Equal( 4f, config.dynamicPixelScale );
    }

    [Theory]
    [InlineData( 0, 225, 1f )]
    [InlineData( 400, -1, 1f )]
    [InlineData( 400, 225, 0f )]
    [InlineData( 400, 225, float.NaN )]
    public void RejectsOutOfRangeSettings ( int resolutionX, int resolutionY, float dynamicPixelScale )
    {
        var config = new GameConfig { resolutionX = resolutionX, resolutionY = resolutionY, dynamicPixelScale = dynamicPixelScale };

        Assert.Throws< InvalidDataException >( () => config.Validate( "config.yaml" ) );
    }

    [Fact]
    public void FixedSizeKeepsTheResolutionAndLetterboxes ()
    {
        var fit = ViewFit.Compute( Config(), 1913, 1007, 2f );

        Assert.Equal( new ViewFit( 400, 225, SDL.LOGICAL_PRESENTATION_LETTERBOX ), fit );
    }

    [Fact]
    public void PixelPerfectScalesByWholeNumbers ()
    {
        var fit = ViewFit.Compute( Config( pixelPerfect: true ), 1913, 1007, 2f );

        Assert.Equal( new ViewFit( 400, 225, SDL.LOGICAL_PRESENTATION_INTEGER_SCALE ), fit );
    }

    [Fact]
    public void DynamicSizeFollowsTheWindowInPoints ()
    {
        // 800x450 points on a 2x display: 4 points per game pixel is 8 output pixels.
        var fit = ViewFit.Compute( Config( dynamicSize: true ), 1600, 900, 2f );

        Assert.Equal( new ViewFit( 200, 112, SDL.LOGICAL_PRESENTATION_LETTERBOX ), fit );
    }

    [Fact]
    public void PixelPerfectDynamicSizeRoundsPixelsToWholeOutputPixels ()
    {
        // 4 points at 1.25x is 5 output pixels; 2.2 points at 1x rounds to 2.
        Assert.Equal( new ViewFit( 200, 100, SDL.LOGICAL_PRESENTATION_INTEGER_SCALE ),
            ViewFit.Compute( Config( pixelPerfect: true, dynamicSize: true ), 1000, 500, 1.25f ) );
        Assert.Equal( new ViewFit( 500, 250, SDL.LOGICAL_PRESENTATION_INTEGER_SCALE ),
            ViewFit.Compute( Config( pixelPerfect: true, dynamicSize: true, dynamicPixelScale: 2.2f ), 1000, 500, 1f ) );
    }

    [Fact]
    public void DynamicSizeIsNeverEmpty ()
    {
        var fit = ViewFit.Compute( Config( dynamicSize: true ), 3, 2, 1f );

        Assert.Equal( ( 1, 1 ), ( fit.Width, fit.Height ) );
    }

    [Fact]
    public void FixedWindowOpensAtTheLargestWholeMultipleThatFits ()
    {
        Assert.Equal( ( 1200, 675 ), ViewFit.InitialWindowSize( Config(), 1512, 945 ) );
        Assert.Equal( ( 2000, 1125 ), ViewFit.InitialWindowSize( Config(), 2560, 1415 ) );
    }

    [Fact]
    public void FixedWindowIsAtLeastOneTimesTheResolution ()
    {
        Assert.Equal( ( 400, 225 ), ViewFit.InitialWindowSize( Config(), 300, 200 ) );
    }

    [Fact]
    public void DynamicWindowOpensAtItsPixelScaleAndShrinksToFit ()
    {
        Assert.Equal( ( 1600, 900 ), ViewFit.InitialWindowSize( Config( dynamicSize: true ), 2560, 1415 ) );
        Assert.Equal( ( 1360, 765 ), ViewFit.InitialWindowSize( Config( dynamicSize: true ), 1512, 945 ) );
    }

    [Fact]
    public void UnknownScreenUsesTheConfiguredSize ()
    {
        Assert.Equal( ( 400, 225 ), ViewFit.InitialWindowSize( Config(), 0, 0 ) );
        Assert.Equal( ( 1600, 900 ), ViewFit.InitialWindowSize( Config( dynamicSize: true ), 0, 0 ) );
    }
}
