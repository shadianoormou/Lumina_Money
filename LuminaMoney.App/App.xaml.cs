namespace LuminaMoney.App;
public partial class App : Application
{
 private readonly AppShell _shell;
 private readonly Services.AppLockService _security;
 private readonly Services.IAppUpdateService _updates;
 private readonly Services.BillReminderService _reminders;
 private readonly Services.FinanceDatabase _database;
 private readonly Services.FinanceSyncService _sync;
 private readonly ViewModels.AppViewModel _appViewModel;
 private readonly SemaphoreSlim _foregroundGate=new(1,1);
 private bool _notificationWarningShown;
 public App(AppShell shell,Services.AppLockService security,Services.IAppUpdateService updates,Services.BillReminderService reminders,Services.FinanceDatabase database,Services.FinanceSyncService sync,ViewModels.AppViewModel appViewModel){InitializeComponent();UserAppTheme=AppTheme.Dark;_shell=shell;_security=security;_updates=updates;_reminders=reminders;_database=database;_sync=sync;_appViewModel=appViewModel;}
 protected override Window CreateWindow(IActivationState? activationState)=>new(_shell);
 protected override void OnStart(){base.OnStart();MainThread.BeginInvokeOnMainThread(RunForegroundChecksAsync);}
 protected override void OnSleep(){base.OnSleep();_security.MarkBackgrounded();}
 protected override void OnResume(){base.OnResume();MainThread.BeginInvokeOnMainThread(RunForegroundChecksAsync);}
 protected override void OnAppLinkRequestReceived(Uri uri)
 {
  base.OnAppLinkRequestReceived(uri);
  if(uri.Scheme=="luminamoney"&&uri.Host=="bank")MainThread.BeginInvokeOnMainThread(OpenBankReturnAsync);
 }
 private static async void OpenBankReturnAsync()
 {
  try
  {
   var shell=Shell.Current;if(shell is null)return;
   if(!shell.CurrentState.Location.OriginalString.EndsWith("/settings",StringComparison.OrdinalIgnoreCase))await shell.GoToAsync("settings");
  }
  catch(InvalidOperationException){ }
 }
 private async void RunForegroundChecksAsync()
 {
  if(!await _foregroundGate.WaitAsync(0))return;
  try
  {
   await _security.EnsureUnlockedAsync();
   await _updates.CheckAsync();
   if(await _sync.TrySyncAsync())await _appViewModel.ReloadAfterExternalSyncAsync();
   if(!_reminders.IsEnabled)return;
   if(_reminders.CanSendNotifications)
   {
    _notificationWarningShown=false;
    var data=await _database.LoadAsync();
    await _reminders.RescheduleAsync(data.Bills);
    return;
   }
   if(_notificationWarningShown)return;
   _notificationWarningShown=true;
   var open=await _shell.DisplayAlertAsync("Notifications are off","Bill reminders cannot be delivered because Lumina Money notifications are blocked. Allow notifications in device settings to keep reminders active.","Open settings","Later");
   if(open)await _reminders.OpenNotificationSettingsAsync();
  }
  catch(Exception){/* Foreground health checks must never stop app launch. */}
  finally{_foregroundGate.Release();}
 }
}
