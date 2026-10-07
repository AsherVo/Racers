using Android.App;
using Android.Content.PM;
using Engine;
using Racers;
using Org.Libsdl.App;

namespace AndroidHost;

[Activity(
    Label = "Hello Sprite",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleInstance,
    AlwaysRetainTaskState = true,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize |
        ConfigChanges.ScreenLayout | ConfigChanges.Keyboard | ConfigChanges.KeyboardHidden |
        ConfigChanges.Navigation | ConfigChanges.UiMode | ConfigChanges.Density)]
public class MainActivity : SDLActivity
{
    protected override string[] GetLibraries() => ["SDL3"];

    // Runs on SDL's thread; returns when the game ends.
    protected override void Main() => GameHost.Run(() => new RacersGame(), RacersGame.Options);
}
