using Android.App;
using Android.Content;
using Android.OS;
using SkinnyB.Services;

namespace SkinnyB;

[Activity(Exported = true)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "com.skinnyb.app",
    DataHost = "oauth2callback")]
public class OAuthCallbackActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var uri = Intent?.Data?.ToString();
        if (uri is not null)
            GoogleAuthService.HandleCallback(uri);

        // Return to the app
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        StartActivity(intent);
        Finish();
    }
}