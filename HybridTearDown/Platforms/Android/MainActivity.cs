using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using HybridTearDown.Services;

namespace HybridTearDown;

// ---------------------------------------------------------------------------
// TC08 PART A — the attribute below keeps Orientation/ScreenSize in
// ConfigurationChanges, so Android hands the app an OnConfigurationChanged
// callback and the activity (and its WebView) survives a rotation.
//
// TC08 PART B — to force Android to destroy and rebuild the activity on every
// rotation, temporarily reduce the attribute to:
//
//     ConfigurationChanges = ConfigChanges.UiMode | ConfigChanges.Density
//
// then rebuild. Restore this attribute afterwards; Part B is a test
// configuration, not how the app ships.
// ---------------------------------------------------------------------------

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
     ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
         ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]

//[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    //ConfigurationChanges = ConfigChanges.UiMode | ConfigChanges.Density)]


public class MainActivity : MauiAppCompatActivity
{
    private Orientation _lastOrientation = Orientation.Undefined;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // savedInstanceState is non-null when Android rebuilt this activity
        // after destroying it — the Part B path. On a cold start it is null.
        var wasRecreated = savedInstanceState is not null;

        _lastOrientation = Resources?.Configuration?.Orientation ?? Orientation.Undefined;

        if (wasRecreated)
        {
            TeardownDiagnostics.MarkOrientationChanged(Describe(_lastOrientation), activityRecreated: true);
        }
    }

    // Called only for configuration changes listed in ConfigurationChanges above.
    // If this fires, the activity was NOT recreated — the Part A path.
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);

        var orientation = newConfig.Orientation;

        if (orientation != _lastOrientation && orientation != Orientation.Undefined)
        {
            _lastOrientation = orientation;
            TeardownDiagnostics.MarkOrientationChanged(Describe(orientation), activityRecreated: false);
        }
    }

    private static string Describe(Orientation orientation) => orientation switch
    {
        Orientation.Portrait => "portrait",
        Orientation.Landscape => "landscape",
        _ => "undefined"
    };
}
