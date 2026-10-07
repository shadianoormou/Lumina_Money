using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LuminaMoney.Core;

namespace LuminaMoney.Api.Services;

public sealed record StoreVerificationResult(bool IsActive, string Status, string OriginalTransactionId, DateTime? ExpiresUtc);

public interface IStorePurchaseVerifier
{
    bool IsConfigured { get; }
    Task<StoreVerificationResult> VerifyAsync(Guid userId, VerifySubscriptionRequest request, CancellationToken ct);
}

public sealed class StorePurchaseVerifier(IHttpClientFactory clients, IConfiguration configuration) : IStorePurchaseVerifier
{
    private readonly string? _appId = configuration["Subscriptions:Iaphub:AppId"];
    private readonly string? _serverApiKey = configuration["Subscriptions:Iaphub:ServerApiKey"];
    private readonly string _environment = configuration["Subscriptions:Iaphub:Environment"] ?? "production";
    private readonly HashSet<string> _products = configuration.GetSection("Subscriptions:AllowedProducts").Get<string[]>()?.ToHashSet(StringComparer.Ordinal) ?? [];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_appId) && !string.IsNullOrWhiteSpace(_serverApiKey)
        && _environment is "production" or "staging" && _products.Count > 0;

    public async Task<StoreVerificationResult> VerifyAsync(Guid userId, VerifySubscriptionRequest request, CancellationToken ct)
    {
        if (!IsConfigured) throw new InvalidOperationException("IAPHUB store verification is not configured.");
        if (request.Store is not ("apple" or "google") || !_products.Contains(request.ProductId) || string.IsNullOrWhiteSpace(request.PurchaseToken) || request.PurchaseToken.Length > 512)
            throw new ArgumentException("The store purchase is invalid.", nameof(request));

        var platform = request.Store == "apple" ? "ios" : "android";
        var url = $"https://api.iaphub.com/v1/app/{Uri.EscapeDataString(_appId!)}/user/{Uri.EscapeDataString(userId.ToString())}?platform={platform}&environment={Uri.EscapeDataString(_environment)}";
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", _serverApiKey);
        using var response = await clients.CreateClient("store-verification").SendAsync(message, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<IaphubUserPayload>(cancellationToken: ct)
            ?? throw new InvalidOperationException("IAPHUB returned an invalid user response.");
        var product = payload.ActiveProducts.FirstOrDefault(x => string.Equals(x.Sku, request.ProductId, StringComparison.Ordinal)
            && string.Equals(x.Purchase, request.PurchaseToken, StringComparison.Ordinal));
        if (product is null) throw new ArgumentException("The purchase is not active for this user.", nameof(request));
        var expiration = product.ExpirationDate?.ToUniversalTime();
        var active = expiration is null || expiration > DateTime.UtcNow;
        var transactionId = product.OriginalPurchase ?? product.Purchase;
        if (string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 240) throw new InvalidOperationException("IAPHUB returned an invalid purchase identity.");
        return new(active, active ? "active" : "expired", transactionId, expiration);
    }

    private sealed class IaphubUserPayload
    {
        [JsonPropertyName("activeProducts")] public IaphubActiveProductPayload[] ActiveProducts { get; set; } = [];
    }
    private sealed record IaphubActiveProductPayload(
        [property: JsonPropertyName("sku")] string Sku,
        [property: JsonPropertyName("purchase")] string Purchase,
        [property: JsonPropertyName("originalPurchase")] string? OriginalPurchase,
        [property: JsonPropertyName("expirationDate")] DateTime? ExpirationDate);
}
