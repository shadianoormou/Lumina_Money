using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;
namespace LuminaMoney.App.Pages;
public partial class AccountEditorPage : ContentPage
{
    public AccountEditorPage(AppViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override async void OnAppearing() { base.OnAppearing(); await PageMotion.EnterAsync(MotionRoot); }
    private async void BackTapped(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("..");
}
