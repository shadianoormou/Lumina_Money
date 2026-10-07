using LuminaMoney.App.Services;

namespace LuminaMoney.App.Pages;

public partial class LockPage : ContentPage
{
    private readonly AppLockService _security;
    private bool _authenticating;
    public LockPage(AppLockService security) { InitializeComponent(); _security = security; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(180);
        await UnlockAsync();
    }

    protected override bool OnBackButtonPressed() => true;
    private async void OnUnlockClicked(object? sender, EventArgs e) => await UnlockAsync();

    private async Task UnlockAsync()
    {
        if (_authenticating) return; _authenticating = true; StatusLabel.Text = "Waiting for secure verification…";
        try
        {
            var result = await _security.AuthenticateAsync("Verify to view your accounts, budget and activity");
            StatusLabel.Text = result.Message;
            if (result.Success) await _security.DismissLockAsync(this);
        }
        finally { _authenticating = false; }
    }

    protected override void OnDisappearing() { base.OnDisappearing(); _security.LockPageClosed(); }
}
