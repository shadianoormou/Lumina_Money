using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaMoney.App.Services;
using LuminaMoney.Core;

namespace LuminaMoney.App.ViewModels;

public partial class AuthViewModel(FinanceApiClient api, FinanceSyncService sync, SessionService session) : ObservableObject
{
    [ObservableProperty] public partial string Email { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string DisplayName { get; set; } = "";
    [ObservableProperty] public partial string ResetCode { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial string Notice { get; set; } = "";
    [ObservableProperty] public partial bool IsRegistering { get; set; }
    [ObservableProperty] public partial bool IsRecovering { get; set; }
    [ObservableProperty] public partial bool IsResetCodeSent { get; set; }
    [ObservableProperty] public partial bool IsVerifyingEmail { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }

    public string ActionText => IsVerifyingEmail ? "Verify email" : IsRecovering ? IsResetCodeSent ? "Set new password" : "Send reset code" : IsRegistering ? "Create secure account" : "Sign in";
    public string SwitchText => IsRegistering ? "Already have an account? Sign in" : "New here? Create an account";
    public string PasswordLabel => IsRecovering ? "NEW PASSWORD" : "PASSWORD";
    public bool ShowPasswordField => !IsVerifyingEmail && (!IsRecovering || IsResetCodeSent);
    public bool ShowCodeField => IsVerifyingEmail || IsRecovering && IsResetCodeSent;
    public bool ShowForgotPassword => !IsRegistering && !IsRecovering && !IsVerifyingEmail;
    public bool ShowModeSwitch => !IsRecovering && !IsVerifyingEmail;
    public bool ShowBackToSignIn => IsRecovering || IsVerifyingEmail;
    public bool ShowResendVerification => IsVerifyingEmail;

    partial void OnIsRegisteringChanged(bool value) => NotifyModeChanged();
    partial void OnIsRecoveringChanged(bool value) => NotifyModeChanged();
    partial void OnIsResetCodeSentChanged(bool value) => NotifyModeChanged();
    partial void OnIsVerifyingEmailChanged(bool value) => NotifyModeChanged();

    [RelayCommand]
    private void SwitchMode()
    {
        IsRegistering = !IsRegistering;
        IsRecovering = IsVerifyingEmail = IsResetCodeSent = false;
        ResetCode = Error = Notice = "";
    }

    [RelayCommand]
    private void ForgotPassword()
    {
        IsRegistering = IsVerifyingEmail = false;
        IsRecovering = true;
        IsResetCodeSent = false;
        Password = ResetCode = Error = "";
        Notice = "Enter your registered email. We will send a private six-digit recovery code.";
    }

    [RelayCommand]
    private void BackToSignIn()
    {
        IsRecovering = IsVerifyingEmail = IsResetCodeSent = IsRegistering = false;
        Password = ResetCode = Error = Notice = "";
    }

    [RelayCommand]
    private async Task ResendVerificationAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Email)) return;
        IsBusy = true; Error = "";
        try { Notice = (await api.ResendEmailVerificationAsync(Email.Trim())).Message; }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or InvalidOperationException) { Error = FriendlyError(ex); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsBusy) return;
        Error = ""; Notice = "";
        if (string.IsNullOrWhiteSpace(Email)) { Error = "Email is required."; return; }
        IsBusy = true;
        try
        {
            if (IsVerifyingEmail)
            {
                if (ResetCode.Trim().Length != 6) { Error = "Enter the six-digit verification code."; return; }
                await api.VerifyEmailAsync(new(Email.Trim(), ResetCode.Trim()));
                await sync.TrySyncAsync();
                await Shell.Current.GoToAsync("//main/dashboard");
                return;
            }

            if (IsRecovering)
            {
                if (!IsResetCodeSent)
                {
                    Notice = (await api.RequestPasswordResetAsync(Email.Trim())).Message;
                    IsResetCodeSent = true;
                    return;
                }
                if (ResetCode.Trim().Length != 6 || string.IsNullOrWhiteSpace(Password)) { Error = "Enter the six-digit code and a strong new password."; return; }
                await api.ResetPasswordAsync(new(Email.Trim(), ResetCode.Trim(), Password));
                session.Clear();
                IsRecovering = IsResetCodeSent = false;
                Password = ResetCode = "";
                Notice = "Password changed securely. Sign in with your new password.";
                return;
            }

            if (string.IsNullOrWhiteSpace(Password)) { Error = "Password is required."; return; }
            var auth = IsRegistering
                ? await api.RegisterAsync(new RegisterRequest(Email.Trim(), Password, DisplayName.Trim()))
                : await api.LoginAsync(new LoginRequest(Email.Trim(), Password));
            if (auth.RequiresEmailVerification)
            {
                IsRegistering = false;
                IsVerifyingEmail = true;
                ResetCode = "";
                Notice = "A six-digit verification code was sent to your email.";
                return;
            }
            await sync.TrySyncAsync();
            await Shell.Current.GoToAsync("//main/dashboard");
        }
        catch (Exception ex) when (ex is TaskCanceledException or HttpRequestException or InvalidOperationException)
        {
            Error = FriendlyError(ex);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task ContinueOfflineAsync() => Shell.Current.GoToAsync("//main/dashboard");

    public async Task TryResumeAsync()
    {
        if (!await session.HasSessionAsync()) return;
        if (await session.RequiresEmailVerificationAsync())
        {
            Email = await session.GetEmailAsync() ?? Email;
            IsRegistering = IsRecovering = false;
            IsVerifyingEmail = true;
            Notice = "Verify your email to finish securing this account.";
            return;
        }
        await sync.TrySyncAsync();
        await Shell.Current.GoToAsync("//main/dashboard");
    }

    private void NotifyModeChanged()
    {
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(SwitchText));
        OnPropertyChanged(nameof(PasswordLabel));
        OnPropertyChanged(nameof(ShowPasswordField));
        OnPropertyChanged(nameof(ShowCodeField));
        OnPropertyChanged(nameof(ShowForgotPassword));
        OnPropertyChanged(nameof(ShowModeSwitch));
        OnPropertyChanged(nameof(ShowBackToSignIn));
        OnPropertyChanged(nameof(ShowResendVerification));
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        TaskCanceledException => "The finance server did not respond. Please try again.",
        HttpRequestException => "Cannot reach the finance server. Check your connection and try again.",
        _ => ex.Message
    };
}
