using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LuminaMoney.Api.Services;

public sealed record BankConnectionStart(string ExternalConnectionId, string AuthorizationUri, string Environment, DateTime ConsentExpiresUtc);
public sealed record ImportedBankAccount(string ExternalId, string Type, string AccountType, string Currency, IReadOnlyList<string> HolderNames);
public sealed record ImportedBankTransaction(string ExternalId, string AccountExternalId, DateTime Timestamp, string Description,
    string Currency, long AmountInMinor, string Status, string? MerchantName, string? CategoryName);
public sealed record BankDataImport(IReadOnlyList<ImportedBankAccount> Accounts, IReadOnlyList<ImportedBankTransaction> Transactions, bool IsPending);

public interface IBankConnectionProvider
{
    string Name { get; }
    string Environment { get; }
    bool IsConfigured { get; }
    Task<BankConnectionStart> StartAsync(Guid userId, string displayName, string email, string? userIp, CancellationToken ct);
    Task<bool> IsAuthorisedAsync(string externalConnectionId, string? userIp, CancellationToken ct);
    Task<BankDataImport> ImportAsync(string externalConnectionId, string? userIp, CancellationToken ct);
}

public sealed class TrueLayerBankProvider(IHttpClientFactory clients, IConfiguration configuration) : IBankConnectionProvider
{
    private readonly string? _clientId = configuration["Banking:TrueLayer:ClientId"];
    private readonly string? _clientSecret = configuration["Banking:TrueLayer:ClientSecret"];
    private readonly string? _returnUri = configuration["Banking:TrueLayer:ReturnUri"];
    private readonly bool _enabled = configuration.GetValue("Banking:TrueLayer:Enabled", false);
    private readonly string _authBaseUrl = configuration["Banking:TrueLayer:AuthBaseUrl"] ?? "https://auth.truelayer-sandbox.com";
    private readonly string _apiBaseUrl = configuration["Banking:TrueLayer:ApiBaseUrl"] ?? "https://api.truelayer-sandbox.com";
    private readonly int _lookbackDays = Math.Clamp(configuration.GetValue("Banking:TrueLayer:TransactionLookbackDays", 365), 30, 730);
    private readonly int _maxPages = Math.Clamp(configuration.GetValue("Banking:TrueLayer:MaxTransactionPages", 20), 1, 100);
    private readonly int _pollAttempts = Math.Clamp(configuration.GetValue("Banking:TrueLayer:PollAttempts", 12), 1, 60);

    public string Name => "TrueLayer";
    public string Environment => _apiBaseUrl.Contains("sandbox", StringComparison.OrdinalIgnoreCase) ? "sandbox" : "production";
    public bool IsConfigured => _enabled && !string.IsNullOrWhiteSpace(_clientId) && !string.IsNullOrWhiteSpace(_clientSecret)
        && IsHttps(_returnUri) && IsHttps(_authBaseUrl) && IsHttps(_apiBaseUrl);

    public async Task<BankConnectionStart> StartAsync(Guid userId, string displayName, string email, string? userIp, CancellationToken ct)
    {
        EnsureConfigured();
        var token = await GetAccessTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBaseUrl.TrimEnd('/')}/v3/data-connections");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        AddUserIp(request, userIp);
        request.Content = JsonContent.Create(new
        {
            scopes = new[] { "accounts", "balance", "transactions" },
            provider_selection = new { type = "user_selected", filter = new { countries = new[] { "GB" }, release_channel = "public", customer_segments = new[] { "retail" } } },
            user = new { id = userId.ToString(), name = displayName, email },
            user_consent = new { type = "authorization_flow_captured" },
            hosted_page = new { type = "authorization_flow", return_uri = _returnUri, country_code = "GB", language_code = "en" },
            data_access_type = "recurring",
            metadata = new Dictionary<string, string> { ["lumina_user_id"] = userId.ToString() }
        });
        using var response = await clients.CreateClient("truelayer").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<CreateConnectionPayload>(cancellationToken: ct)
            ?? throw new InvalidOperationException("TrueLayer returned an invalid connection response.");
        if (!Guid.TryParse(payload.Id, out _) || !Uri.TryCreate(payload.HostedPage?.Uri, UriKind.Absolute, out var authorizationUri) || authorizationUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("TrueLayer did not return a secure authorisation link.");
        return new(payload.Id, authorizationUri.ToString(), Environment, DateTime.UtcNow.AddDays(90));
    }

    public async Task<bool> IsAuthorisedAsync(string externalConnectionId, string? userIp, CancellationToken ct)
    {
        EnsureConfigured();
        if (!Guid.TryParse(externalConnectionId, out _)) return false;
        var token = await GetAccessTokenAsync(ct);
        using var request = CreateConnectionRequest(HttpMethod.Get, "/v3/connected-accounts", token, externalConnectionId, userIp);
        using var response = await clients.CreateClient("truelayer").SendAsync(request, ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<BankDataImport> ImportAsync(string externalConnectionId, string? userIp, CancellationToken ct)
    {
        EnsureConfigured();
        if (!Guid.TryParse(externalConnectionId, out _)) throw new InvalidOperationException("The bank connection identifier is invalid.");
        var token = await GetAccessTokenAsync(ct);
        var accounts = await GetAccountsAsync(token, externalConnectionId, userIp, ct);
        var transactions = new List<ImportedBankTransaction>();
        var pending = false;
        foreach (var account in accounts)
        {
            string? cursor = null;
            for (var page = 0; page < _maxPages; page++)
            {
                var result = await GetTransactionPageAsync(token, externalConnectionId, account.ExternalId, cursor, userIp, ct);
                transactions.AddRange(result.Items.Select(x => new ImportedBankTransaction(x.Id, account.ExternalId, x.Timestamp,
                    x.Description, x.Currency, x.AmountInMinor, x.Status, x.Enrichment?.MerchantName, x.Enrichment?.TransactionCategory?.CategoryName)));
                if (result.IsPending) { pending = true; break; }
                cursor = result.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor)) break;
            }
        }
        return new BankDataImport(accounts, transactions, pending);
    }

    private async Task<IReadOnlyList<ImportedBankAccount>> GetAccountsAsync(string token, string connectionId, string? userIp, CancellationToken ct)
    {
        using var request = CreateConnectionRequest(HttpMethod.Get, "/v3/connected-accounts", token, connectionId, userIp);
        using var response = await clients.CreateClient("truelayer").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ConnectedAccountsPayload>(cancellationToken: ct)
            ?? throw new InvalidOperationException("TrueLayer returned an invalid accounts response.");
        return payload.Items.Where(x => !string.IsNullOrWhiteSpace(x.Id) && x.Currency is "GBP" or "EUR")
            .Select(x => new ImportedBankAccount(x.Id, x.Type, x.AccountType, x.Currency, x.AccountHolderNames)).ToList();
    }

    private async Task<TransactionPage> GetTransactionPageAsync(string token, string connectionId, string accountId, string? cursor, string? userIp, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["from"] = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_lookbackDays)).ToString("yyyy-MM-dd"),
            ["to"] = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            ["page_size"] = 500,
            ["enrichment"] = new { transaction_category = true, merchant_name = true }
        };
        if (!string.IsNullOrWhiteSpace(cursor)) body["cursor"] = cursor;
        var escapedAccount = Uri.EscapeDataString(accountId);
        using var create = CreateConnectionRequest(HttpMethod.Post, $"/v3/connected-accounts/{escapedAccount}/transactions/requests", token, connectionId, userIp);
        create.Content = JsonContent.Create(body);
        using var createResponse = await clients.CreateClient("truelayer").SendAsync(create, ct);
        createResponse.EnsureSuccessStatusCode();
        var accepted = await createResponse.Content.ReadFromJsonAsync<TransactionRequestPayload>(cancellationToken: ct)
            ?? throw new InvalidOperationException("TrueLayer returned an invalid transaction request.");
        if (!Guid.TryParse(accepted.Id, out _)) throw new InvalidOperationException("TrueLayer returned an invalid transaction request identifier.");

        for (var attempt = 0; attempt < _pollAttempts; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(1500, 350 + attempt * 150)), ct);
            using var poll = CreateConnectionRequest(HttpMethod.Get,
                $"/v3/connected-accounts/{escapedAccount}/transactions/requests/{Uri.EscapeDataString(accepted.Id)}", token, connectionId, userIp);
            using var pollResponse = await clients.CreateClient("truelayer").SendAsync(poll, ct);
            pollResponse.EnsureSuccessStatusCode();
            var result = await pollResponse.Content.ReadFromJsonAsync<TransactionRequestPayload>(cancellationToken: ct)
                ?? throw new InvalidOperationException("TrueLayer returned an invalid transaction result.");
            if (result.Status == "failed") throw new InvalidOperationException($"TrueLayer transaction sync failed: {result.FailureReason ?? "provider_error"}.");
            if (result.Status == "completed") return new(result.Result?.Items ?? [], result.Result?.Pagination?.NextCursor, false);
        }
        return new([], cursor, true);
    }

    private HttpRequestMessage CreateConnectionRequest(HttpMethod method, string path, string token, string connectionId, string? userIp)
    {
        var request = new HttpRequestMessage(method, $"{_apiBaseUrl.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Connection-Id", connectionId);
        AddUserIp(request, userIp);
        return request;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_authBaseUrl.TrimEnd('/')}/connect/token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["client_id"] = _clientId!, ["client_secret"] = _clientSecret!, ["scope"] = "data"
        });
        using var response = await clients.CreateClient("truelayer").SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<TokenPayload>(cancellationToken: ct);
        return string.IsNullOrWhiteSpace(payload?.AccessToken) ? throw new InvalidOperationException("TrueLayer did not issue an access token.") : payload.AccessToken;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured) throw new InvalidOperationException("TrueLayer is not configured.");
    }

    private static void AddUserIp(HttpRequestMessage request, string? userIp)
    {
        if (!string.IsNullOrWhiteSpace(userIp)) request.Headers.TryAddWithoutValidation("Tl-User-IP", userIp);
    }

    private static bool IsHttps(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    private sealed record TokenPayload([property: JsonPropertyName("access_token")] string AccessToken);
    private sealed record CreateConnectionPayload(string Id, string Status, [property: JsonPropertyName("hosted_page")] HostedPagePayload? HostedPage);
    private sealed record HostedPagePayload(string Uri);
    private sealed class ConnectedAccountsPayload { public ConnectedAccountPayload[] Items { get; init; } = []; }
    private sealed record ConnectedAccountPayload(string Id, string Type, [property: JsonPropertyName("account_type")] string AccountType,
        string Currency, [property: JsonPropertyName("account_holder_names")] string[] AccountHolderNames);
    private sealed class TransactionRequestPayload
    {
        public string Id { get; init; } = "";
        public string Status { get; init; } = "";
        public TransactionResultPayload? Result { get; init; }
        [JsonPropertyName("failure_reason")] public string? FailureReason { get; init; }
    }
    private sealed class TransactionResultPayload { public TransactionPayload[] Items { get; init; } = []; public PaginationPayload? Pagination { get; init; } }
    private sealed record PaginationPayload([property: JsonPropertyName("next_cursor")] string? NextCursor);
    private sealed record TransactionPayload(string Id, DateTime Timestamp, string Description, string Currency,
        [property: JsonPropertyName("amount_in_minor")] long AmountInMinor, string Status, TransactionEnrichmentPayload? Enrichment);
    private sealed record TransactionEnrichmentPayload([property: JsonPropertyName("merchant_name")] string? MerchantName,
        [property: JsonPropertyName("transaction_category")] TransactionCategoryPayload? TransactionCategory);
    private sealed record TransactionCategoryPayload([property: JsonPropertyName("category_name")] string? CategoryName);
    private sealed record TransactionPage(IReadOnlyList<TransactionPayload> Items, string? NextCursor, bool IsPending);
}
