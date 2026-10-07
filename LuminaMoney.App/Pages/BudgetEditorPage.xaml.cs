using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;
namespace LuminaMoney.App.Pages;
public partial class BudgetEditorPage : ContentPage
{
    public BudgetEditorPage(AppViewModel vm) { InitializeComponent(); BindingContext = vm; }
    protected override async void OnAppearing() { base.OnAppearing(); await PageMotion.EnterAsync(MotionRoot); }
}
