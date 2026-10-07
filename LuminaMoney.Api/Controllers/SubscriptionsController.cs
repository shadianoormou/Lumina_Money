using LuminaMoney.Api.Data;
using LuminaMoney.Api.Services;
using LuminaMoney.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LuminaMoney.Api.Controllers;

[ApiController, Authorize, Route("api/v1/subscriptions")]
public sealed class SubscriptionsController(FinanceDbContext db, IStorePurchaseVerifier verifier, IConfiguration configuration) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("uid")!.Value);
    private readonly HashSet<string> _allowedProducts = configuration.GetSection("Subscriptions:AllowedProducts").Get<string[]>()?.ToHashSet(StringComparer.Ordinal) ?? [];

    [HttpGet("status")]
    public async Task<ActionResult<SubscriptionStatusResponse>> Status(CancellationToken ct)
    {
        var row = await db.SubscriptionEntitlements.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == UserId, ct);
        var active = row is not null && row.Status == "active" && (row.ExpiresUtc is null || row.ExpiresUtc > DateTime.UtcNow);
        return Ok(new SubscriptionStatusResponse(active, active ? "Lumina Plus" : "Free", row?.Status ?? "inactive", row?.Store ?? "none", row?.ExpiresUtc, row?.LastVerifiedUtc));
    }

    [HttpPost("verify")]
    public async Task<ActionResult<SubscriptionStatusResponse>> Verify(VerifySubscriptionRequest request, CancellationToken ct)
    {
        if (!verifier.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiError("store_verification_unavailable", "Store receipt verification is not configured on this environment."));
        StoreVerificationResult verified;
        try { verified = await verifier.VerifyAsync(UserId, request with { Store = request.Store.Trim().ToLowerInvariant(), ProductId = request.ProductId.Trim() }, ct); }
        catch (ArgumentException) { return BadRequest(new ApiError("invalid_store_purchase", "The store purchase could not be verified.")); }

        var row = await db.SubscriptionEntitlements.SingleOrDefaultAsync(x => x.UserId == UserId, ct);
        if (row is null) { row = new SubscriptionEntitlementEntity { UserId = UserId }; db.SubscriptionEntitlements.Add(row); }
        row.Store = request.Store.Trim().ToLowerInvariant(); row.ProductId = request.ProductId.Trim();
        row.OriginalTransactionId = verified.OriginalTransactionId; row.Status = verified.IsActive ? "active" : verified.Status;
        row.ExpiresUtc = verified.ExpiresUtc; row.LastVerifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new SubscriptionStatusResponse(verified.IsActive, verified.IsActive ? "Lumina Plus" : "Free", row.Status, row.Store, row.ExpiresUtc, row.LastVerifiedUtc));
    }

    [AllowAnonymous, HttpPost("iaphub/webhook")]
    public async Task<ActionResult> IaphubWebhook(IaphubWebhookPayload payload, CancellationToken ct)
    {
        var configuredToken = configuration["Subscriptions:Iaphub:WebhookToken"];
        var suppliedToken = Request.Headers["X-Auth-Token"].ToString();
        if (string.IsNullOrWhiteSpace(configuredToken) || !FixedTimeEquals(configuredToken, suppliedToken)) return Unauthorized();
        if (string.IsNullOrWhiteSpace(payload.Id) || string.IsNullOrWhiteSpace(payload.Type) || payload.Data.ValueKind != JsonValueKind.Object) return BadRequest();
        if (!TryText(payload.Data, "userId", out var userIdText) || !Guid.TryParse(userIdText, out var userId)) return Ok();
        if (!TryText(payload.Data, "productSku", out var sku) || !_allowedProducts.Contains(sku)) return Ok();
        if (!await db.Users.AnyAsync(x => x.Id == userId, ct)) return Ok();

        var inactiveEvent = payload.Type is "refund" or "subscription_expire" or "subscription_grace_period_expire";
        var isActive = payload.Data.TryGetProperty("isSubscriptionActive", out var activeValue) && activeValue.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? activeValue.GetBoolean() : !inactiveEvent;
        DateTime? expiresUtc = null;
        if (TryText(payload.Data, "expirationDate", out var expirationText) && DateTime.TryParse(expirationText, out var parsedExpiration)) expiresUtc = parsedExpiration.ToUniversalTime();
        if (expiresUtc is not null && expiresUtc <= DateTime.UtcNow) isActive = false;
        _ = TryText(payload.Data, "platform", out var platform);
        var store = platform.Equals("ios", StringComparison.OrdinalIgnoreCase) ? "apple" : "google";
        var purchaseId = TryText(payload.Data, "originalPurchase", out var original) ? original
            : TryText(payload.Data, "purchase", out var purchase) ? purchase : payload.Id;
        if (purchaseId.Length > 240) return BadRequest();

        var row = await db.SubscriptionEntitlements.SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (row is null) { row = new SubscriptionEntitlementEntity { UserId = userId }; db.SubscriptionEntitlements.Add(row); }
        row.Store = store; row.ProductId = sku; row.OriginalTransactionId = purchaseId;
        row.Status = isActive ? "active" : inactiveEvent ? "expired" : "inactive"; row.ExpiresUtc = expiresUtc; row.LastVerifiedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return Ok();
    }

    private static bool TryText(JsonElement value, string property, out string result)
    {
        if (value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String && item.GetString() is { } text) { result = text; return true; }
        result = ""; return false;
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.UTF8.GetBytes(expected); var b = Encoding.UTF8.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public sealed record IaphubWebhookPayload(string Id, string Type, DateTime CreatedDate, string Version, JsonElement Data);
}
