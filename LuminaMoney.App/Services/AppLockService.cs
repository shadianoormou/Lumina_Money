using LuminaMoney.App.Pages;
using Maui.Biometric;
using Microsoft.Extensions.DependencyInjection;

namespace LuminaMoney.App.Services;

public sealed class AppLockService(IBiometricAuthentication biometric, SessionService session, IServiceProvider services)
{
    private const string EnabledKey = "lumina.biometric_lock";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _requiresUnlock = true;
    private bool _isShowing;

    public bool IsEnabled => Preferences.Default.Get(EnabledKey, false);

    public async Task<bool> IsAvailableAsync() => await biometric.IsAvailableAsync(Authenticator.Biometric);

    public async Task<(bool Success, string Message)> SetEnabledAsync(bool enabled, CancellationToken ct = default)
    {
        if (enabled && !await IsAvailableAsync()) return (false, "No enrolled Face ID, Touch ID or fingerprint is available on this device.");
        if (enabled || IsEnabled)
        {
            var result = await AuthenticateAsync(enabled ? "Confirm biometric app lock" : "Confirm before disabling app lock", ct);
            if (!result.Success) return result;
        }
        Preferences.Default.Set(EnabledKey, enabled);
        _requiresUnlock = false;
        return (true, enabled ? "Biometric app lock enabled" : "Biometric app lock disabled");
    }

    public void MarkBackgrounded()
    {
        if (IsEnabled) _requiresUnlock = true;
    }

    public void MarkUnlocked() => _requiresUnlock = false;

    public async Task EnsureUnlockedAsync()
    {
        if (!IsEnabled || !_requiresUnlock || !await session.HasSessionAsync()) return;
        await _gate.WaitAsync();
        try
        {
            if (!IsEnabled || !_requiresUnlock || _isShowing || Shell.Current?.Navigation is null) return;
            _isShowing = true;
            var page = services.GetRequiredService<LockPage>();
            await Shell.Current.Navigation.PushModalAsync(page, false);
        }
        catch (InvalidOperationException) { _isShowing = false; }
        finally { _gate.Release(); }
    }

    public async Task<(bool Success, string Message)> AuthenticateAsync(string reason, CancellationToken ct = default)
    {
        if (!await IsAvailableAsync()) return (false, "Biometric authentication is unavailable. Unlock the device and check biometric enrolment.");
        var request = new AuthenticationRequest("Unlock Lumina Money", reason)
        {
            Authenticators = Authenticator.Biometric,
            CancelTitle = "Cancel",
            ConfirmationRequired = true
        };
        var result = await biometric.AuthenticateAsync(request, ct);
        return result.IsSuccessful ? (true, "Unlocked") : (false, string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Authentication was not completed." : result.ErrorMessage);
    }

    public async Task DismissLockAsync(Page page)
    {
        _requiresUnlock = false; _isShowing = false;
        if (Shell.Current?.Navigation.ModalStack.Contains(page) == true) await Shell.Current.Navigation.PopModalAsync(false);
    }

    public void LockPageClosed() => _isShowing = false;
}
