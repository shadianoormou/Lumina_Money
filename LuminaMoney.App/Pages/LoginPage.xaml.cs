using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;
namespace LuminaMoney.App.Pages;
public partial class LoginPage : ContentPage
{
    public LoginPage(AuthViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override async void OnAppearing() { base.OnAppearing(); await PageMotion.EnterAsync(MotionRoot); await ((AuthViewModel)BindingContext).TryResumeAsync(); }
    protected override void OnDisappearing() { EmailEntry.Unfocus(); CodeEntry.Unfocus(); PasswordEntry.Unfocus(); base.OnDisappearing(); }
}
