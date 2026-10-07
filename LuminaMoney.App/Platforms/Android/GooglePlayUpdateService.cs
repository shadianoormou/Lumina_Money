using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;
using GmsTask = Android.Gms.Tasks.Task;
using MauiApplication = Microsoft.Maui.Controls.Application;

namespace LuminaMoney.App.Services;

/// <summary>
/// Implements the official Google Play Core update flow. Google Play only returns
/// update metadata when this exact application id/signature was installed by Play.
/// </summary>
public sealed class GooglePlayUpdateService : Java.Lang.Object, IAppUpdateService, IInstallStateUpdatedListener
{
    private readonly IAppUpdateManager _manager;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppUpdateInfo? _availableUpdate;
    private int _preferredUpdateType = AppUpdateType.Flexible;
    private int? _promptedVersion;

    public GooglePlayUpdateService()
    {
        _manager = AppUpdateManagerFactory.Create(Android.App.Application.Context);
        _manager.RegisterListener(this);
        State = DefaultState();
    }

    public AppUpdateState State { get; private set; }
    public event EventHandler<AppUpdateState>? StateChanged;

    public async Task CheckAsync(bool userInitiated = false)
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            SetState(State with { IsBusy = true, Status = "Checking Google Play…" });
            var value = await AwaitGoogleTaskAsync(_manager.GetAppUpdateInfo());
            var info = value as AppUpdateInfo ?? throw new InvalidOperationException("Google Play returned no update metadata.");
            var availability = info.UpdateAvailability();

            if (info.InstallStatus() == InstallStatus.Downloaded)
            {
                _availableUpdate = info;
                SetState(new(true, true, true, false, false, info.AvailableVersionCode(), "Update downloaded · ready to install"));
                await PromptToCompleteAsync();
                return;
            }

            var immediateInProgress = availability == UpdateAvailability.DeveloperTriggeredUpdateInProgress;
            var updateAvailable = availability == UpdateAvailability.UpdateAvailable || immediateInProgress;
            if (!updateAvailable)
            {
                _availableUpdate = null;
                SetState(DefaultState() with { Status = $"Up to date · version {AppInfo.Current.VersionString}" });
                if (userInitiated) await ShowAlertAsync("Lumina Money is up to date", $"You have the latest Google Play version ({AppInfo.Current.VersionString}).", "Done");
                return;
            }

            var critical = info.UpdatePriority() >= 4;
            _preferredUpdateType = critical ? AppUpdateType.Immediate : AppUpdateType.Flexible;
            if (!info.IsUpdateTypeAllowed(AppUpdateOptions.DefaultOptions(_preferredUpdateType)))
            {
                _preferredUpdateType = AppUpdateType.Immediate;
                if (!info.IsUpdateTypeAllowed(AppUpdateOptions.DefaultOptions(_preferredUpdateType)))
                    throw new InvalidOperationException("Google Play cannot start this update on the current device.");
            }

            _availableUpdate = info;
            SetState(new(true, true, false, false, critical, info.AvailableVersionCode(),
                critical ? "Important security update available" : "New Google Play update available"));

            if (immediateInProgress)
            {
                await StartCoreAsync();
                return;
            }

            if (userInitiated || _promptedVersion != info.AvailableVersionCode())
            {
                _promptedVersion = info.AvailableVersionCode();
                var accepted = await ShowChoiceAsync(
                    critical ? "Important update required" : "A new Lumina Money update is available",
                    critical
                        ? "Install the latest security and reliability update from Google Play now."
                        : "Update inside the app to get the latest fixes and features.",
                    "Update now", "Later");
                if (accepted) await StartCoreAsync();
            }
        }
        catch (Exception)
        {
            _availableUpdate = null;
            var storeMessage = "Update checks activate after the signed app is installed from Google Play.";
            SetState(DefaultState() with { Status = userInitiated ? storeMessage : $"Google Play updates · version {AppInfo.Current.VersionString}" });
            if (userInitiated) await ShowAlertAsync(
                "Google Play update check",
                $"{storeMessage}\n\nIf this is already a Play-installed build, check the Play Store, internet connection, battery and free storage, then try again.",
                "Done");
        }
        finally
        {
            if (State.IsBusy) SetState(State with { IsBusy = false });
            _gate.Release();
        }
    }

    public async Task StartAsync()
    {
        if (_availableUpdate is null)
        {
            await CheckAsync(true);
            return;
        }

        await StartCoreAsync();
    }

    public async Task CompleteAsync()
    {
        try
        {
            SetState(State with { IsBusy = true, Status = "Installing update…" });
            await AwaitGoogleTaskAsync(_manager.CompleteUpdate());
        }
        catch (Exception ex)
        {
            SetState(State with { IsBusy = false, Status = "Update installation could not start" });
            await ShowAlertAsync("Unable to install update", FriendlyFailureMessage(ex), "Done");
        }
    }

    public void OnStateUpdate(InstallState? state)
    {
        if (state is null) return;
        var installStatus = state.InstallStatus();
        if (installStatus == InstallStatus.Downloading)
        {
            var percentage = state.TotalBytesToDownload() <= 0
                ? 0
                : (int)Math.Round(state.BytesDownloaded() * 100d / state.TotalBytesToDownload());
            SetState(State with { IsBusy = true, Status = $"Downloading update · {percentage}%" });
        }
        else if (installStatus == InstallStatus.Downloaded)
        {
            SetState(State with { IsAvailable = true, IsReadyToInstall = true, IsBusy = false, Status = "Update downloaded · ready to install" });
            MainThread.BeginInvokeOnMainThread(async () => await PromptToCompleteAsync());
        }
        else if (installStatus == InstallStatus.Failed)
        {
            SetState(State with { IsBusy = false, Status = $"Google Play update failed ({state.InstallErrorCode()})" });
        }
        else if (installStatus == InstallStatus.Canceled)
        {
            SetState(State with { IsBusy = false, Status = "Update postponed" });
        }
    }

    private async Task StartCoreAsync()
    {
        var activity = MainActivity.Current;
        if (_availableUpdate is null || activity is null)
            throw new InvalidOperationException("The update screen is not ready yet. Please try again.");

        SetState(State with { IsBusy = true, Status = _preferredUpdateType == AppUpdateType.Immediate ? "Opening required update…" : "Starting update download…" });
        try
        {
            var flow = _manager.StartUpdateFlow(
                _availableUpdate,
                activity,
                AppUpdateOptions.DefaultOptions(_preferredUpdateType))
                ?? throw new InvalidOperationException("Google Play could not create the update flow.");
            await AwaitGoogleTaskAsync(flow);
        }
        finally
        {
            SetState(State with { IsBusy = false });
        }
    }

    private async Task PromptToCompleteAsync()
    {
        var restart = await ShowChoiceAsync(
            "Update ready",
            "The latest Lumina Money version has downloaded. Restart now to finish installing it.",
            "Restart & install", "Later");
        if (restart) await CompleteAsync();
    }

    private void SetState(AppUpdateState value)
    {
        State = value;
        MainThread.BeginInvokeOnMainThread(() => StateChanged?.Invoke(this, value));
    }

    private static AppUpdateState DefaultState() => new(
        true, false, false, false, false, null,
        $"Google Play updates · version {AppInfo.Current.VersionString}");

    private static async Task<Java.Lang.Object?> AwaitGoogleTaskAsync(GmsTask task)
    {
        var source = new TaskCompletionSource<Java.Lang.Object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        task.AddOnCompleteListener(new GoogleTaskCompleteListener(source));
        return await source.Task;
    }

    private static Task<bool> ShowChoiceAsync(string title, string message, string accept, string cancel) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Shell.Current ?? MauiApplication.Current?.Windows.FirstOrDefault()?.Page;
            return page is not null && await page.DisplayAlertAsync(title, message, accept, cancel);
        });

    private static Task ShowAlertAsync(string title, string message, string cancel) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Shell.Current ?? MauiApplication.Current?.Windows.FirstOrDefault()?.Page;
            if (page is not null) await page.DisplayAlertAsync(title, message, cancel);
        });

    private static string FriendlyFailureMessage(Exception exception)
    {
        _ = exception;
        return "Google Play could not complete the update. Check the Play Store, internet connection, battery and free storage, then try again.";
    }

    private sealed class GoogleTaskCompleteListener(TaskCompletionSource<Java.Lang.Object?> source) : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(GmsTask task)
        {
            if (task.IsSuccessful) source.TrySetResult(task.Result);
            else if (task.IsCanceled) source.TrySetCanceled();
            else source.TrySetException(new InvalidOperationException(task.Exception?.LocalizedMessage ?? "Google Play task failed."));
        }
    }
}
