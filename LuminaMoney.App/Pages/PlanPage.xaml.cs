using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;
namespace LuminaMoney.App.Pages;
public partial class PlanPage : ContentPage
{
    public PlanPage(AppViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override async void OnAppearing() { base.OnAppearing(); await ((AppViewModel)BindingContext).LoadIfStaleAsync(); await PageMotion.EnterAsync(MotionRoot); }
}
