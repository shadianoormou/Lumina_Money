using Foundation;
using UIKit;

namespace LuminaMoney.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        if (!Uri.TryCreate(url.AbsoluteString, UriKind.Absolute, out var uri)) return false;
        Microsoft.Maui.Controls.Application.Current?.SendOnAppLinkRequestReceived(uri);
        return true;
    }
}
