using System.Reflection;
using Iaphub;
using Iaphub.Sdk;
using LuminaMoney.Core;

namespace LuminaMoney.App.Services;

public sealed class StoreBillingService(SessionService session, FinanceApiClient api)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _appId = Metadata("IaphubAppId");
    private readonly string _apiKey = Metadata("IaphubApiKey");
    private string? _initializedUser;

    public bool IsConfigured => (DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS)
        && !string.IsNullOrWhiteSpace(_appId) && !string.IsNullOrWhiteSpace(_apiKey)
        && _appId != "not-configured" && _apiKey != "not-configured";

    public async Task<IReadOnlyList<StoreProduct>> GetProductsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var products = await IaphubSdk.GetProductsForSaleAsync();
        return products.Where(x => !string.IsNullOrWhiteSpace(x.Sku)).Select(x => new StoreProduct(
            x.Sku, string.IsNullOrWhiteSpace(x.LocalizedTitle) ? x.Alias ?? x.Sku : x.LocalizedTitle,
            x.LocalizedDescription ?? "Premium Lumina Money membership", x.LocalizedPrice ?? x.Price?.ToString("C") ?? "—",
            x.SubscriptionDuration ?? "Subscription", x)).ToList();
    }

    public async Task<SubscriptionStatusResponse> BuyAsync(StoreProduct product, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var receipt = await IaphubSdk.BuyAsync(product.Source.Sku);
        if (string.IsNullOrWhiteSpace(receipt.Purchase)) throw new InvalidOperationException("The store did not return a verified purchase identity.");
        return await api.VerifySubscriptionAsync(new(StoreName(receipt.Platform), receipt.Sku, receipt.Purchase), ct);
    }

    public async Task<SubscriptionStatusResponse> RestoreAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct); await IaphubSdk.RestoreAsync();
        var active = (await IaphubSdk.GetActiveProductsAsync()).OrderByDescending(x => x.ExpirationDate).FirstOrDefault()
            ?? throw new InvalidOperationException("No active App Store or Google Play subscription was found.");
        if (string.IsNullOrWhiteSpace(active.Purchase)) throw new InvalidOperationException("The restored subscription has no purchase identity.");
        return await api.VerifySubscriptionAsync(new(StoreName(active.Platform), active.Sku, active.Purchase), ct);
    }

    public async Task ManageAsync(CancellationToken ct = default) { await EnsureInitializedAsync(ct); await IaphubSdk.ShowManageSubscriptionsAsync(); }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("App Store and Google Play billing credentials are not configured in this build.");
        var userId = await session.GetUserIdAsync();
        if (!Guid.TryParse(userId, out _)) throw new UnauthorizedAccessException("Sign in before purchasing a subscription.");
        if (_initializedUser == userId) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_initializedUser == userId) return;
            if (_initializedUser is not null) await IaphubSdk.StopAsync();
            await IaphubSdk.StartAsync(_appId, _apiKey, userId, allowAnonymousPurchase: false, enableStorekitV2: true, lang: "en");
            _initializedUser = userId;
        }
        finally { _gate.Release(); }
    }

    private static string StoreName(string? platform) => platform?.Contains("ios", StringComparison.OrdinalIgnoreCase) == true || platform?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true ? "apple" : "google";
    private static string Metadata(string key) => typeof(StoreBillingService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().SingleOrDefault(x => x.Key == key)?.Value ?? "";
}

public sealed record StoreProduct(string Sku, string Title, string Description, string Price, string Period, IaphubProduct Source);
