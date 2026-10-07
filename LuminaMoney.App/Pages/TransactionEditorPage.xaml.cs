using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;
namespace LuminaMoney.App.Pages;
public partial class TransactionEditorPage : ContentPage
{
    private readonly AppViewModel _vm;
    public TransactionEditorPage(AppViewModel vm) { InitializeComponent(); BindingContext = _vm = vm; }
    protected override async void OnAppearing() { base.OnAppearing(); await _vm.LoadAsync(); if (_vm.IsTransfer) TransferClicked(null, EventArgs.Empty); else if (_vm.IsIncome) IncomeClicked(null, EventArgs.Empty); else ExpenseClicked(null, EventArgs.Empty); await PageMotion.EnterAsync(MotionRoot); }
    private void ExpenseClicked(object? sender, EventArgs e)
    {
        _vm.IsIncome = false; _vm.IsTransfer = false; _vm.CanSplitTransaction = true; _vm.ShowSingleCategory = !_vm.IsSplitTransaction; ExpenseButton.BackgroundColor = Color.FromArgb("#3479E8"); ExpenseButton.TextColor = Colors.White;
        IncomeButton.BackgroundColor = Color.FromArgb("#162B48"); IncomeButton.TextColor = Color.FromArgb("#A9C9FF");
        TransferButton.BackgroundColor = Color.FromArgb("#162B48"); TransferButton.TextColor = Color.FromArgb("#A9C9FF");
    }
    private void IncomeClicked(object? sender, EventArgs e)
    {
        _vm.IsIncome = true; _vm.IsTransfer = false; _vm.CanSplitTransaction = false; _vm.IsSplitTransaction = false; _vm.ShowSingleCategory = true; IncomeButton.BackgroundColor = Color.FromArgb("#3479E8"); IncomeButton.TextColor = Colors.White;
        ExpenseButton.BackgroundColor = Color.FromArgb("#162B48"); ExpenseButton.TextColor = Color.FromArgb("#A9C9FF");
        TransferButton.BackgroundColor = Color.FromArgb("#162B48"); TransferButton.TextColor = Color.FromArgb("#A9C9FF");
    }
    private void TransferClicked(object? sender, EventArgs e)
    {
        _vm.IsIncome = false; _vm.IsTransfer = true; _vm.CanSplitTransaction = false; _vm.IsSplitTransaction = false; _vm.ShowSingleCategory = false; TransferButton.BackgroundColor = Color.FromArgb("#3479E8"); TransferButton.TextColor = Colors.White;
        ExpenseButton.BackgroundColor = Color.FromArgb("#162B48"); ExpenseButton.TextColor = Color.FromArgb("#A9C9FF"); IncomeButton.BackgroundColor = Color.FromArgb("#162B48"); IncomeButton.TextColor = Color.FromArgb("#A9C9FF");
    }
}
