using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;

namespace LuminaMoney.App.Pages;

public partial class MembershipPage : ContentPage
{
    private readonly MembershipViewModel _viewModel;
    public MembershipPage(MembershipViewModel viewModel) { InitializeComponent(); BindingContext = _viewModel = viewModel; }
    protected override async void OnAppearing() { base.OnAppearing(); await _viewModel.LoadAsync(); await PageMotion.EnterAsync(MotionRoot); }
}
