namespace LuminaMoney.App.Services;

public sealed record AppUpdateState(
    bool IsSupported,
    bool IsAvailable,
    bool IsReadyToInstall,
    bool IsBusy,
    bool IsCritical,
    int? AvailableVersionCode,
    string Status);

public interface IAppUpdateService
{
    AppUpdateState State { get; }
    event EventHandler<AppUpdateState>? StateChanged;
    Task CheckAsync(bool userInitiated = false);
    Task StartAsync();
    Task CompleteAsync();
}

public sealed class UnsupportedAppUpdateService : IAppUpdateService
{
    public AppUpdateState State { get; } = new(
        false, false, false, false, false, null,
        $"App Store updates · version {AppInfo.Current.VersionString}");

    public event EventHandler<AppUpdateState>? StateChanged
    {
        add { }
        remove { }
    }

    public Task CheckAsync(bool userInitiated = false) => Task.CompletedTask;
    public Task StartAsync() => Task.CompletedTask;
    public Task CompleteAsync() => Task.CompletedTask;
}
