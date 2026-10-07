using LuminaMoney.Core;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace LuminaMoney.App.Services;

public sealed class BillReminderService(INotificationService notifications)
{
    private const string EnabledKey = "lumina.bill_reminders";
    private const string LeadDaysKey = "lumina.bill_reminder_days";
    private const string OfferShownKey = "lumina.bill_reminder_offer_shown";
    private const string GroupName = "lumina-bills";

    public bool IsEnabled => Preferences.Default.Get(EnabledKey, false);
    public int LeadDays => Math.Clamp(Preferences.Default.Get(LeadDaysKey, 3), 0, 14);
    public bool ShouldOfferAfterBillCreated => !IsEnabled && !Preferences.Default.Get(OfferShownKey, false);
    public void MarkEnableOfferShown() => Preferences.Default.Set(OfferShownKey, true);

    public bool CanSendNotifications
    {
        get
        {
            if (!notifications.IsSupported) return false;
#if ANDROID
            var manager = AndroidX.Core.App.NotificationManagerCompat.From(Android.App.Application.Context);
            return manager?.AreNotificationsEnabled() == true;
#else
            return true;
#endif
        }
    }

    public async Task<(bool Success, string Message)> SetEnabledAsync(bool enabled, IEnumerable<RecurringBill> bills)
    {
        if (enabled)
        {
            if (!notifications.IsSupported) return (false, "Scheduled notifications are not supported on this device.");
            if (!await notifications.RequestNotificationPermission(new NotificationPermission())) return (false, "Notification permission was not granted.");
            if (!CanSendNotifications) return (false, "Notifications are blocked in device settings. Open settings and allow Lumina Money notifications.");
        }
        Preferences.Default.Set(EnabledKey, enabled);
        await RescheduleAsync(bills);
        return (true, enabled ? "Bill reminders enabled" : "Bill reminders disabled");
    }

    public async Task SetLeadDaysAsync(int days, IEnumerable<RecurringBill> bills)
    {
        Preferences.Default.Set(LeadDaysKey, Math.Clamp(days, 0, 14));
        await RescheduleAsync(bills);
    }

    public async Task RescheduleAsync(IEnumerable<RecurringBill> bills)
    {
        if (!notifications.IsSupported) return;
        var pending = await notifications.GetPendingNotificationList();
        var ids = pending.Where(x => x.Group == GroupName).Select(x => x.NotificationId).ToArray();
        if (ids.Length > 0) notifications.Cancel(ids);
        if (!IsEnabled || !CanSendNotifications) return;

        foreach (var bill in bills.Where(x => x.NextDue.Date >= DateTime.Today).OrderBy(x => x.NextDue).Take(64))
        {
            var notifyAt = bill.NextDue.Date.AddDays(-LeadDays).AddHours(9);
            if (notifyAt <= DateTime.Now) notifyAt = DateTime.Now.AddMinutes(1);
            var request = new NotificationRequest
            {
                NotificationId = NotificationId(bill.Id),
                Title = bill.NextDue.Date == DateTime.Today ? $"{bill.Name} is due today" : $"{bill.Name} is coming up",
                Description = $"£{bill.Amount:N2} due {bill.NextDue:ddd, dd MMM}. Open Lumina Money to review your plan.",
                Group = GroupName,
                ReturningData = $"bill:{bill.Id}",
                Schedule = new NotificationRequestSchedule { NotifyTime = notifyAt }
            };
            await notifications.Show(request);
        }
    }

    public Task OpenNotificationSettingsAsync()
    {
#if ANDROID
        Android.Content.Intent intent;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            intent = new Android.Content.Intent(Android.Provider.Settings.ActionAppNotificationSettings)
                .PutExtra(Android.Provider.Settings.ExtraAppPackage, AppInfo.Current.PackageName);
        }
        else
        {
            intent = new Android.Content.Intent(
                Android.Provider.Settings.ActionApplicationDetailsSettings,
                Android.Net.Uri.Parse($"package:{AppInfo.Current.PackageName}"));
        }
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        Android.App.Application.Context.StartActivity(intent);
#else
        AppInfo.Current.ShowSettingsUI();
#endif
        return Task.CompletedTask;
    }

    private static int NotificationId(Guid id) => 100_000 + (BitConverter.ToInt32(id.ToByteArray(), 0) & 0x3fffffff);
}
