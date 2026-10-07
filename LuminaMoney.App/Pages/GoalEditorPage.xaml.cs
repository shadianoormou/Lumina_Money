using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;
namespace LuminaMoney.App.Pages;
public partial class GoalEditorPage : ContentPage
{
    public GoalEditorPage(AppViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override async void OnAppearing() { base.OnAppearing(); await PageMotion.EnterAsync(MotionRoot); }
}
