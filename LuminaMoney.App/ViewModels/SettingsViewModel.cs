using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaMoney.App.Services;
using LuminaMoney.Core;

namespace LuminaMoney.App.ViewModels;

public partial class SettingsViewModel(
    FinanceSyncService sync, FinanceApiClient api, SessionService session, FinanceDatabase database,
    AppViewModel app, AppLockService security, BillReminderService reminders, IAppUpdateService updates) : ObservableObject
{
    [ObservableProperty] public partial string DisplayName { get; set; } = "Offline profile";
    [ObservableProperty] public partial string Email { get; set; } = "Local data only";
    [ObservableProperty] public partial string Initials { get; set; } = "LM";
    [ObservableProperty] public partial string SyncStatus { get; set; } = "Checking sync status…";
    [ObservableProperty] public partial string ActionStatus { get; set; } = "";
    [ObservableProperty] public partial bool HasActionStatus { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial bool IsSignedIn { get; set; }
    [ObservableProperty] public partial bool IsReducedMotion { get; set; } = Preferences.Default.Get("lumina.reduced_motion", false);
    [ObservableProperty] public partial bool IsBiometricEnabled { get; set; }
    [ObservableProperty] public partial string BiometricAction { get; set; } = "Enable biometric lock";
    [ObservableProperty] public partial string BiometricStatus { get; set; } = "Checking device security…";
    [ObservableProperty] public partial bool AreRemindersEnabled { get; set; }
    [ObservableProperty] public partial string ReminderAction { get; set; } = "Enable bill reminders";
    [ObservableProperty] public partial string ReminderStatus { get; set; } = "Notifications are off";
    [ObservableProperty] public partial bool ShowNotificationSettings { get; set; }
    [ObservableProperty] public partial ReminderLeadOption? SelectedReminderLead { get; set; }
    [ObservableProperty] public partial string UpdateStatus { get; set; } = "Checking Google Play…";
    [ObservableProperty] public partial string UpdateAction { get; set; } = "Install update";
    [ObservableProperty] public partial bool ShowUpdateAction { get; set; }
    [ObservableProperty] public partial string PlanName { get; set; } = "Free";
    [ObservableProperty] public partial string PlanStatus { get; set; } = "Checking entitlement…";
    [ObservableProperty] public partial string BankStatus { get; set; } = "Checking Open Banking…";
    [ObservableProperty] public partial bool CanConnectBank { get; set; }
    [ObservableProperty] public partial bool HasBankConnections { get; set; }
    private IReadOnlyList<BankConnectionResponse> _bankConnections = [];
    [ObservableProperty] public partial string DeletePassword { get; set; } = "";
    [ObservableProperty] public partial string DeleteConfirmation { get; set; } = "";

    public IReadOnlyList<ReminderLeadOption> ReminderLeadOptions { get; } =
        [new(0, "On the due date"), new(1, "1 day before"), new(3, "3 days before"), new(7, "7 days before")];

    partial void OnIsReducedMotionChanged(bool value) => Preferences.Default.Set("lumina.reduced_motion", value);

    public async Task LoadAsync()
    {
        IsSignedIn = await session.HasSessionAsync();
        DisplayName = await session.GetDisplayNameAsync() ?? "Offline profile";
        Email = await session.GetEmailAsync() ?? "Local data only";
        Initials = MakeInitials(DisplayName);
        SyncStatus = IsSignedIn ? "Cloud sync ready" : "Offline vault active";
        IsBiometricEnabled = security.IsEnabled;
        BiometricAction = IsBiometricEnabled ? "Disable biometric lock" : "Enable biometric lock";
        BiometricStatus = IsBiometricEnabled ? "Locks whenever Lumina leaves the foreground" : await security.IsAvailableAsync() ? "Face ID, Touch ID or fingerprint is available" : "No enrolled biometric found on this device";
        SelectedReminderLead = ReminderLeadOptions.FirstOrDefault(x => x.Days == reminders.LeadDays) ?? ReminderLeadOptions[2];
        RefreshReminderState();
        RefreshUpdateState();
        if (IsSignedIn) await LoadCommercialStatusAsync();
        else { PlanName = "Offline"; PlanStatus = "Sign in to check entitlement"; BankStatus = "Sign in to connect a bank"; CanConnectBank = false; HasBankConnections = false; _bankConnections = []; }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsBusy) return; IsBusy = true; SyncStatus = "Syncing protected session…";
        try
        {
            var completed = await sync.TrySyncAsync();
            SyncStatus = completed ? $"Up to date · {DateTime.Now:t}" : sync.LastError ?? (IsSignedIn ? "Offline — changes remain queued" : "Sign in to enable cloud sync");
            if (completed) await app.ReloadAfterExternalSyncAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleBiometricAsync()
    {
        if (IsBusy) return; IsBusy = true;
        try
        {
            var result = await security.SetEnabledAsync(!security.IsEnabled);
            ShowStatus(result.Message); IsBiometricEnabled = security.IsEnabled;
            BiometricAction = IsBiometricEnabled ? "Disable biometric lock" : "Enable biometric lock";
            BiometricStatus = IsBiometricEnabled ? "Locks whenever Lumina leaves the foreground" : "Biometric app lock is off";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleRemindersAsync()
    {
        if (IsBusy) return; IsBusy = true;
        try
        {
            var data = await database.LoadAsync(); var result = await reminders.SetEnabledAsync(!reminders.IsEnabled, data.Bills);
            ShowStatus(result.Message); RefreshReminderState();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SaveReminderTimingAsync()
    {
        if (SelectedReminderLead is null) return; IsBusy = true;
        try { var data = await database.LoadAsync(); await reminders.SetLeadDaysAsync(SelectedReminderLead.Days, data.Bills); RefreshReminderState(); if (!AreRemindersEnabled) ReminderStatus = "Timing saved for when reminders are enabled"; ShowStatus("Reminder timing saved"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OpenNotificationSettingsAsync()
    {
        await reminders.OpenNotificationSettingsAsync();
        ShowStatus("Allow notifications, then return to Lumina Money");
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await updates.CheckAsync(true); RefreshUpdateState(); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (updates.State.IsReadyToInstall) await updates.CompleteAsync();
        else await updates.StartAsync();
        RefreshUpdateState();
    }

    [RelayCommand]
    private async Task RefreshCommercialStatusAsync()
    {
        if (!IsSignedIn || IsBusy) return; IsBusy = true;
        try { await LoadCommercialStatusAsync(); ShowStatus("Commercial services refreshed"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task OpenMembershipAsync() => await Shell.Current.GoToAsync("membership");

    [RelayCommand]
    private Task OpenPrivacyAsync() => Browser.Default.OpenAsync(api.PublicUri("legal/privacy"), BrowserLaunchMode.SystemPreferred);

    [RelayCommand]
    private Task OpenTermsAsync() => Browser.Default.OpenAsync(api.PublicUri("legal/terms"), BrowserLaunchMode.SystemPreferred);

    [RelayCommand]
    private Task OpenSupportAsync() => Browser.Default.OpenAsync(api.PublicUri("legal/support"), BrowserLaunchMode.SystemPreferred);

    [RelayCommand]
    private Task OpenDeletionHelpAsync() => Browser.Default.OpenAsync(api.PublicUri("legal/account-deletion"), BrowserLaunchMode.SystemPreferred);

    [RelayCommand]
    private async Task ConnectBankAsync()
    {
        if (!CanConnectBank || IsBusy) { ShowStatus("TrueLayer production or sandbox credentials are not configured on the API."); return; }
        IsBusy = true;
        try
        {
            var connection = await api.StartBankConnectionAsync();
            await Browser.Default.OpenAsync(new Uri(connection.AuthorizationUri), BrowserLaunchMode.SystemPreferred);
            BankStatus = "Authorisation opened · return here when your bank confirms";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { ShowStatus(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DisconnectBanksAsync()
    {
        if (!HasBankConnections || IsBusy) return;
        var confirmed = await Shell.Current.DisplayAlertAsync("Disconnect all linked banks?",
            "Lumina will delete its connection identifiers and stop future bank refreshes. Imported history stays in your account. You can also revoke Lumina consent in each bank's security settings.",
            "Disconnect", "Cancel");
        if (!confirmed) return;
        IsBusy = true;
        try
        {
            foreach (var connection in _bankConnections) await api.DeleteBankConnectionAsync(connection.Id);
            _bankConnections = [];
            HasBankConnections = false;
            BankStatus = "No connected banks";
            ShowStatus("Bank access disconnected; imported history was kept");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { ShowStatus(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ExportCloudDataAsync()
    {
        if (!IsSignedIn || IsBusy) return; IsBusy = true;
        try
        {
            var export = await api.ExportAccountAsync();
            var path = Path.Combine(FileSystem.CacheDirectory, $"lumina-account-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));
            await Share.Default.RequestAsync(new ShareFileRequest("Export my Lumina Money data", new ShareFile(path, "application/json")));
            ShowStatus("Secure account export prepared");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException) { ShowStatus(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DeleteCloudAccountAsync()
    {
        if (!IsSignedIn || IsBusy) return;
        if (DeleteConfirmation.Trim() != "DELETE" || string.IsNullOrEmpty(DeletePassword)) { ShowStatus("Enter your password and type DELETE exactly."); return; }
        if (!await Shell.Current.DisplayAlertAsync("Permanently delete account?", "This removes your SQL Server cloud data, refresh sessions and this device's local vault. It cannot be undone. Export first if you need a copy.", "Delete permanently", "Cancel")) return;
        IsBusy = true;
        try
        {
            await api.DeleteAccountAsync(new(DeletePassword, DeleteConfirmation));
            await database.EraseCurrentProfileAsync(); session.Clear(); app.ResetProfile(); DeletePassword = DeleteConfirmation = "";
            await Shell.Current.GoToAsync("//login");
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { ShowStatus(ex.Message); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        if (!IsSignedIn) { await Shell.Current.GoToAsync("//login"); return; }
        if (!await Shell.Current.DisplayAlertAsync("Sign out?", "Your protected local copy stays on this device and is isolated from every other profile.", "Sign out", "Cancel")) return;
        IsBusy = true;
        try { await api.LogoutAsync(); session.Clear(); app.ResetProfile(); await Shell.Current.GoToAsync("//login"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task EraseLocalCopyAsync()
    {
        if (!await Shell.Current.DisplayAlertAsync("Erase local copy?", "This permanently removes this profile's cached accounts, transactions and queued offline changes from this device. Cloud data is not deleted.", "Erase", "Cancel")) return;
        IsBusy = true;
        try
        {
            if (IsSignedIn) await api.LogoutAsync();
            await database.EraseCurrentProfileAsync(); session.Clear(); app.ResetProfile(); await Shell.Current.GoToAsync("//login");
        }
        finally { IsBusy = false; }
    }

    private async Task LoadCommercialStatusAsync()
    {
        try
        {
            var entitlement = await api.GetSubscriptionStatusAsync();
            PlanName = entitlement.Plan; PlanStatus = entitlement.IsPremium ? $"Active via {entitlement.Source} · renews {entitlement.ExpiresUtc:d}" : "Free plan · no active store entitlement";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { PlanStatus = ex.Message; }
        try
        {
            var provider = await api.GetBankProviderStatusAsync(); CanConnectBank = provider.IsConfigured;
            var connections = await api.GetBankConnectionsAsync();
            if (provider.IsConfigured)
            {
                foreach (var pending in connections.Where(x => x.Status is "pending_authorisation" or "authorisation_required" or "sync_pending" or "provider_unavailable").Take(3))
                    try { await api.RefreshBankConnectionAsync(pending.Id); } catch (HttpRequestException) { }
                connections = await api.GetBankConnectionsAsync();
            }
            _bankConnections = connections;
            HasBankConnections = connections.Count > 0;
            var active = connections.Count(x => x.Status == "active");
            var pendingSync = connections.Count(x => x.Status == "sync_pending");
            var importedTransactions = connections.Sum(x => x.ImportedTransactions);
            BankStatus = active > 0 ? $"{active} active · {importedTransactions:N0} transaction{(importedTransactions == 1 ? "" : "s")} synced"
                : pendingSync > 0 ? $"Bank connected · transaction import is processing"
                : provider.IsConfigured ? $"TrueLayer {provider.Environment} ready" : "TrueLayer credentials not configured";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { CanConnectBank = false; HasBankConnections = false; _bankConnections = []; BankStatus = ex.Message; }
    }

    private void ShowStatus(string message) { ActionStatus = message; HasActionStatus = true; }

    private void RefreshReminderState()
    {
        AreRemindersEnabled = reminders.IsEnabled;
        ReminderAction = AreRemindersEnabled ? "Disable bill reminders" : "Enable bill reminders";
        ShowNotificationSettings = AreRemindersEnabled && !reminders.CanSendNotifications;
        ReminderStatus = !AreRemindersEnabled
            ? "Notifications are off"
            : ShowNotificationSettings
                ? "Blocked by device settings · reminders cannot be delivered"
                : $"Active · scheduled {SelectedReminderLead?.Label.ToLowerInvariant()}";
    }

    private void RefreshUpdateState()
    {
        var state = updates.State;
        UpdateStatus = state.Status;
        ShowUpdateAction = state.IsAvailable;
        UpdateAction = state.IsReadyToInstall ? "Restart & install" : state.IsCritical ? "Install required update" : "Install update";
    }

    private static string MakeInitials(string value)
    {
        var result = string.Concat(value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0])));
        return string.IsNullOrEmpty(result) ? "LM" : result;
    }
}

public sealed record ReminderLeadOption(int Days, string Label);
