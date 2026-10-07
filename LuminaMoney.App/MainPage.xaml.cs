using LuminaMoney.App.ViewModels;
namespace LuminaMoney.App.Pages;
public partial class MainPage : ContentPage
{
 private bool _hasAnimatedIn;
 public MainPage(AppViewModel vm){InitializeComponent();BindingContext=vm;
#if ANDROID
  // MAUI already applies the Android status-bar inset. Avoid adding it a second time.
  MotionRoot.Padding=new Thickness(20,12,20,40);
#endif
 }
 protected override async void OnAppearing(){base.OnAppearing();SetDashboardStatusBar();try{await ((AppViewModel)BindingContext).LoadIfStaleAsync();if(_hasAnimatedIn)return;_hasAnimatedIn=true;var views=new VisualElement[]{Header,GettingStartedCard,SnapshotArt,FirstSteps,FreshMonth,FreshCashFlowCard,Overview};if(Preferences.Default.Get("lumina.reduced_motion",false)){foreach(var view in views){view.Opacity=1;view.TranslationY=0;view.Scale=1;}return;}foreach(var view in views){if(!view.IsVisible){view.Opacity=1;view.TranslationY=0;view.Scale=1;continue;}view.Opacity=0;view.TranslationY=16;view.Scale=.985;}var animations=new List<Task>(views.Length);foreach(var view in views){if(!view.IsVisible)continue;animations.Add(AnimateSafelyAsync(view));await Task.Delay(65);}await Task.WhenAll(animations);}catch(ObjectDisposedException){}}
 private static void SetDashboardStatusBar(){
#if ANDROID
  var window=MainActivity.Current?.Window;if(window is not null){if(OperatingSystem.IsAndroidVersionAtLeast(30))window.InsetsController?.SetSystemBarsAppearance(0,(int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars);if(!OperatingSystem.IsAndroidVersionAtLeast(35))window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#080C14"));if(!OperatingSystem.IsAndroidVersionAtLeast(30)){var flags=window.DecorView.SystemUiFlags;window.DecorView.SystemUiFlags=flags&~Android.Views.SystemUiFlags.LightStatusBar;}}
#endif
 }
 private static async Task AnimateSafelyAsync(VisualElement view){try{await Task.WhenAll(view.FadeToAsync(1,420,Easing.CubicOut),view.TranslateToAsync(0,0,520,Easing.CubicOut),view.ScaleToAsync(1,480,Easing.CubicOut));}catch(ObjectDisposedException){}catch(InvalidOperationException){}}
}
