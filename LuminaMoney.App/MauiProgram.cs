using LuminaMoney.App.Pages;
using LuminaMoney.App.Services;
using LuminaMoney.App.ViewModels;
using Microsoft.Extensions.Logging;
using System.Reflection;
using Maui.Biometric;
using Plugin.LocalNotification;
namespace LuminaMoney.App;
public static class MauiProgram
{
 public static MauiApp CreateMauiApp(){var builder=MauiApp.CreateBuilder();builder.UseMauiApp<App>().UseBiometricAuthentication().UseLocalNotification().ConfigureFonts(f=>{f.AddFont("OpenSans-Regular.ttf","OpenSansRegular");f.AddFont("OpenSans-Semibold.ttf","OpenSansSemibold");});
#if ANDROID
 Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("LuminaInputSurface",(handler,view)=>handler.PlatformView.BackgroundTintList=Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
 Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("LuminaInputSurface",(handler,view)=>handler.PlatformView.BackgroundTintList=Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
 Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping("LuminaInputSurface",(handler,view)=>handler.PlatformView.BackgroundTintList=Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent));
#endif
#if DEBUG
 builder.Logging.AddDebug();
#endif
 builder.Services.AddSingleton<SessionService>();builder.Services.AddSingleton<LocalDataProtector>();builder.Services.AddSingleton<FinanceDatabase>();
 builder.Services.AddSingleton<AppLockService>();builder.Services.AddSingleton<BillReminderService>();
#if ANDROID
 builder.Services.AddSingleton<IAppUpdateService,GooglePlayUpdateService>();
#else
 builder.Services.AddSingleton<IAppUpdateService,UnsupportedAppUpdateService>();
#endif
 builder.Services.AddSingleton<StoreBillingService>();builder.Services.AddTransient<MembershipViewModel>();
 builder.Services.AddSingleton(_=>{var handler=new HttpClientHandler();
#if DEBUG
 handler.ServerCertificateCustomValidationCallback=(message,certificate,chain,errors)=>message?.RequestUri?.Host is "localhost" or "10.0.2.2";
#endif
#if DEBUG
 var host=DeviceInfo.Platform==DevicePlatform.Android?"10.0.2.2":"localhost";var apiBase=$"https://{host}:7252/api/v1/";
#else
 var apiBase=typeof(MauiProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(x=>x.Key=="LuminaApiBaseUrl")?.Value??throw new InvalidOperationException("Release API URL metadata is missing.");
#endif
 return new HttpClient(handler){BaseAddress=new Uri(apiBase),Timeout=TimeSpan.FromSeconds(20)};});
 builder.Services.AddSingleton<FinanceApiClient>();builder.Services.AddSingleton<FinanceSyncService>();builder.Services.AddSingleton<AppViewModel>();builder.Services.AddSingleton<AuthViewModel>();builder.Services.AddSingleton<SettingsViewModel>();builder.Services.AddSingleton<AppShell>();builder.Services.AddSingleton<LoginPage>();builder.Services.AddSingleton<MainPage>();builder.Services.AddSingleton<BudgetPage>();builder.Services.AddSingleton<TransactionsPage>();builder.Services.AddSingleton<PlanPage>();builder.Services.AddSingleton<AccountsPage>();builder.Services.AddTransient<TransactionEditorPage>();builder.Services.AddTransient<AccountEditorPage>();builder.Services.AddTransient<BudgetEditorPage>();builder.Services.AddTransient<GoalEditorPage>();builder.Services.AddTransient<BillEditorPage>();builder.Services.AddTransient<BudgetMovePage>();builder.Services.AddTransient<BudgetAssignPage>();builder.Services.AddTransient<GoalFundPage>();builder.Services.AddTransient<ReportsPage>();builder.Services.AddTransient<SettingsPage>();builder.Services.AddTransient<MembershipPage>();builder.Services.AddTransient<LockPage>();return builder.Build();}
}
