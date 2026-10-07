using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using LuminaMoney.Api.Data;
using LuminaMoney.Api.Services;
using LuminaMoney.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LuminaMoney.Tests;

public sealed class ApiIntegrationTests : IClassFixture<FinanceApiFactory>
{
    private readonly FinanceApiFactory _factory;
    public ApiIntegrationTests(FinanceApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Public_legal_and_support_pages_are_available_without_an_account()
    {
        using var client = _factory.CreateHttpsClient();

        var privacy = await client.GetStringAsync("/legal/privacy");
        var terms = await client.GetStringAsync("/legal/terms");
        var support = await client.GetStringAsync("/legal/support");
        var deletion = await client.GetStringAsync("/legal/account-deletion");

        Assert.Contains("Who controls your data", privacy);
        Assert.Contains("does not sell your personal or financial data", privacy);
        Assert.Contains("not a bank", terms);
        Assert.Contains("Forgot password?", support);
        Assert.Contains("Permanently delete account", deletion);
    }

    [Fact]
    public async Task Anonymous_finance_sync_is_rejected()
    {
        using var client = _factory.CreateHttpsClient();
        var response = await client.GetAsync("/api/v1/finance/sync");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registration_returns_identity_and_a_user_owned_starter_plan()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "starter@example.com", "Starter User");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var snapshot = await client.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync");

        Assert.NotNull(snapshot);
        Assert.Equal("starter@example.com", auth.Email);
        Assert.Equal("Starter User", auth.DisplayName);
        Assert.Single(snapshot.Accounts);
        Assert.Equal(3, snapshot.Categories.Count);
    }

    [Fact]
    public async Task Users_cannot_write_transactions_to_another_users_account()
    {
        using var ownerClient = _factory.CreateHttpsClient(); using var attackerClient = _factory.CreateHttpsClient();
        var owner = await RegisterAsync(ownerClient, "owner@example.com", "Owner"); ownerClient.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        var ownerData = await ownerClient.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync");
        var attacker = await RegisterAsync(attackerClient, "attacker@example.com", "Attacker"); attackerClient.DefaultRequestHeaders.Authorization = new("Bearer", attacker.AccessToken);
        var id = Guid.NewGuid(); var request = new UpsertTransactionRequest(id, ownerData!.Accounts[0].Id, null, DateTime.Today, 10, TransactionType.Expense, "Foreign account", "", true);

        var response = await attackerClient.PutAsJsonAsync($"/api/v1/finance/transactions/{id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_account", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Fact]
    public async Task Transfer_requires_a_distinct_owned_destination_and_round_trips()
    {
        using var client = _factory.CreateHttpsClient(); var auth = await RegisterAsync(client, "transfer@example.com", "Transfer User"); client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var snapshot = await client.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync"); var source = snapshot!.Accounts[0];
        var destination = new Account(Guid.NewGuid(), "Savings", AccountType.Savings, "GBP", 0, "#10B981");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/finance/accounts/{destination.Id}", destination)).StatusCode);
        var id = Guid.NewGuid(); var transfer = new UpsertTransactionRequest(id, source.Id, null, DateTime.Today, 75, TransactionType.Transfer, "Transfer to Savings", "", true, destination.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/finance/transactions/{id}", transfer)).StatusCode);
        var refreshed = await client.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync");

        Assert.Equal(destination.Id, refreshed!.Transactions.Single(x => x.Id == id).TransferAccountId);
        Assert.Equal(75, FinanceEngine.Balance(destination, refreshed.Transactions));
    }

    [Fact]
    public async Task Split_expense_total_is_validated_and_persisted()
    {
        using var client = _factory.CreateHttpsClient(); var auth = await RegisterAsync(client, "split@example.com", "Split User"); client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var snapshot = await client.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync"); var account = snapshot!.Accounts[0]; var home = snapshot.Categories[0]; var food = snapshot.Categories[1]; var id = Guid.NewGuid();
        var bad = new UpsertTransactionRequest(id, account.Id, null, DateTime.Today, 100, TransactionType.Expense, "Superstore", "", true, Splits: [new(Guid.NewGuid(), home.Id, 50), new(Guid.NewGuid(), food.Id, 40)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/finance/transactions/{id}", bad)).StatusCode);
        var valid = bad with { Splits = [new(Guid.NewGuid(), home.Id, 60), new(Guid.NewGuid(), food.Id, 40)] };

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/finance/transactions/{id}", valid)).StatusCode);
        var refreshed = await client.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync");

        Assert.Equal(2, refreshed!.Transactions.Single(x => x.Id == id).Splits!.Count);
        Assert.Equal(100, refreshed.Transactions.Single(x => x.Id == id).Splits!.Sum(x => x.Amount));
    }

    [Fact]
    public async Task Bill_category_must_belong_to_the_authenticated_user()
    {
        using var ownerClient = _factory.CreateHttpsClient(); using var attackerClient = _factory.CreateHttpsClient();
        var owner = await RegisterAsync(ownerClient, "bill-owner@example.com", "Bill Owner"); ownerClient.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        var ownerData = await ownerClient.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync");
        var attacker = await RegisterAsync(attackerClient, "bill-attacker@example.com", "Bill Attacker"); attackerClient.DefaultRequestHeaders.Authorization = new("Bearer", attacker.AccessToken);
        var bill = new RecurringBill(Guid.NewGuid(), "Foreign category bill", 25, DateTime.Today.AddDays(5), RecurrenceFrequency.Monthly, ownerData!.Categories[0].Id, false);

        var response = await attackerClient.PutAsJsonAsync($"/api/v1/finance/bills/{bill.Id}", bill);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_category", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Fact]
    public async Task Full_sync_does_not_truncate_large_transaction_history()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "large-history@example.com", "Large History");
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var userId = Guid.Parse(auth.UserId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var accountId = await db.Accounts.Where(x => x.UserId == userId).Select(x => x.Id).SingleAsync();
            db.Transactions.AddRange(Enumerable.Range(0, 10_001).Select(index => new TransactionEntity
            {
                Id = Guid.NewGuid(), UserId = userId, AccountId = accountId, Date = DateTime.Today.AddDays(-(index % 365)),
                Amount = 1, Type = (int)TransactionType.Expense, Payee = $"History {index}", Note = "", IsCleared = true
            }));
            await db.SaveChangesAsync();
        }

        var snapshot = await client.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync");

        Assert.NotNull(snapshot);
        Assert.Equal(10_001, snapshot.Transactions.Count);
    }

    [Fact]
    public async Task Oversized_or_malformed_finance_values_are_rejected_cleanly()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "validation@example.com", "Validation User");
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var invalid = new Account(Guid.NewGuid(), "Broken colour", AccountType.Current, "GBP", 0, "blue");

        var response = await client.PutAsJsonAsync($"/api/v1/finance/accounts/{invalid.Id}", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_account", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Fact]
    public async Task Delete_operations_are_idempotent_for_safe_offline_retries()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "retry-delete@example.com", "Retry Delete");
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var bill = new RecurringBill(Guid.NewGuid(), "Retry-safe bill", 40, DateTime.Today.AddDays(2), RecurrenceFrequency.Monthly, null, false);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/finance/bills/{bill.Id}", bill)).StatusCode);

        var first = await client.DeleteAsync($"/api/v1/finance/bills/{bill.Id}");
        var retry = await client.DeleteAsync($"/api/v1/finance/bills/{bill.Id}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
    }

    [Fact]
    public async Task Email_verification_code_confirms_ownership_and_cannot_be_replayed()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "verify-email@example.com", "Verify Email");
        var requested = await client.PostAsJsonAsync("/api/v1/auth/email/resend", new RequestPasswordResetRequest(auth.Email));
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var email = _factory.Services.GetRequiredService<CapturingAccountEmailSender>();
        var code = email.VerificationCodes[auth.Email];

        var verifiedResponse = await client.PostAsJsonAsync("/api/v1/auth/email/verify", new VerifyEmailRequest(auth.Email, code));
        var replay = await client.PostAsJsonAsync("/api/v1/auth/email/verify", new VerifyEmailRequest(auth.Email, code));

        Assert.Equal(HttpStatusCode.OK, verifiedResponse.StatusCode);
        Assert.False((await verifiedResponse.Content.ReadFromJsonAsync<AuthResponse>())!.RequiresEmailVerification);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.True((await db.Users.SingleAsync(x => x.Email == auth.Email)).EmailConfirmed);
    }

    [Fact]
    public async Task Password_reset_changes_credentials_revokes_sessions_and_consumes_code()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "recover@example.com", "Recover User");
        var requested = await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new RequestPasswordResetRequest(auth.Email));
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var email = _factory.Services.GetRequiredService<CapturingAccountEmailSender>();
        var code = email.PasswordResetCodes[auth.Email];

        var weak = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new ResetPasswordRequest(auth.Email, code, "weak"));
        var reset = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new ResetPasswordRequest(auth.Email, code, "NewStrongPass!2026"));
        var replay = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new ResetPasswordRequest(auth.Email, code, "AnotherPass!2026"));
        var oldRefresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken));
        var oldLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(auth.Email, "StrongPass!2026"));
        var newLogin = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(auth.Email, "NewStrongPass!2026"));

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Bank_disconnect_is_owned_idempotent_and_keeps_imported_history()
    {
        using var ownerClient = _factory.CreateHttpsClient(); using var attackerClient = _factory.CreateHttpsClient();
        var owner = await RegisterAsync(ownerClient, "disconnect-owner@example.com", "Disconnect Owner");
        var attacker = await RegisterAsync(attackerClient, "disconnect-attacker@example.com", "Disconnect Attacker");
        var ownerId = Guid.Parse(owner.UserId); var connectionId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            db.BankConnections.Add(new BankConnectionEntity { Id = connectionId, UserId = ownerId, ExternalConnectionId = Guid.NewGuid().ToString(), Status = "active", Environment = "sandbox", ConsentExpiresUtc = DateTime.UtcNow.AddDays(80) });
            await db.SaveChangesAsync();
        }
        attackerClient.DefaultRequestHeaders.Authorization = new("Bearer", attacker.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await attackerClient.DeleteAsync($"/api/v1/bank-connections/{connectionId}")).StatusCode);
        ownerClient.DefaultRequestHeaders.Authorization = new("Bearer", owner.AccessToken);
        Assert.Single((await ownerClient.GetFromJsonAsync<IReadOnlyList<BankConnectionResponse>>("/api/v1/bank-connections"))!);

        var first = await ownerClient.DeleteAsync($"/api/v1/bank-connections/{connectionId}");
        var retry = await ownerClient.DeleteAsync($"/api/v1/bank-connections/{connectionId}");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
        Assert.Empty((await ownerClient.GetFromJsonAsync<IReadOnlyList<BankConnectionResponse>>("/api/v1/bank-connections"))!);
    }

    [Fact]
    public async Task Reused_refresh_token_revokes_the_rotated_session_family()
    {
        using var client = _factory.CreateHttpsClient(); var original = await RegisterAsync(client, "replay@example.com", "Replay Test");
        var rotatedResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(original.RefreshToken));
        rotatedResponse.EnsureSuccessStatusCode(); var rotated = (await rotatedResponse.Content.ReadFromJsonAsync<AuthResponse>())!;

        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(original.RefreshToken));
        var familyAttempt = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(rotated.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("refresh_token_reuse", (await replay.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, familyAttempt.StatusCode);
    }

    [Fact]
    public async Task Account_export_contains_profile_and_complete_owned_finance_data()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "export@example.com", "Export User");
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);

        var export = await client.GetFromJsonAsync<AccountExportResponse>("/api/v1/account/export");

        Assert.NotNull(export);
        Assert.Equal(auth.UserId, export.Profile.UserId);
        Assert.Equal("export@example.com", export.Profile.Email);
        Assert.Single(export.FinanceData.Accounts);
        Assert.Equal(3, export.FinanceData.Categories.Count);
    }

    [Fact]
    public async Task Account_deletion_requires_reauthentication_and_removes_login()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "delete@example.com", "Delete User");
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        using var wrong = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/account") { Content = JsonContent.Create(new DeleteAccountRequest("WrongPass!2026", "DELETE")) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);
        using var confirmed = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/account") { Content = JsonContent.Create(new DeleteAccountRequest("StrongPass!2026", "DELETE")) };

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(confirmed)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("delete@example.com", "StrongPass!2026"));
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Commercial_integrations_fail_closed_until_server_credentials_are_configured()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "gates@example.com", "Gate User");
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);

        var entitlement = await client.GetFromJsonAsync<SubscriptionStatusResponse>("/api/v1/subscriptions/status");
        var verify = await client.PostAsJsonAsync("/api/v1/subscriptions/verify", new VerifySubscriptionRequest("apple", "lumina_plus_monthly", "untrusted-client-token"));
        var bankStatus = await client.GetFromJsonAsync<BankProviderStatusResponse>("/api/v1/bank-connections/provider");
        var connect = await client.PostAsync("/api/v1/bank-connections/truelayer/start", null);

        Assert.NotNull(entitlement); Assert.False(entitlement.IsPremium); Assert.Equal("Free", entitlement.Plan);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, verify.StatusCode);
        Assert.NotNull(bankStatus); Assert.False(bankStatus.IsConfigured);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, connect.StatusCode);
    }

    [Fact]
    public async Task Authenticated_store_webhooks_update_and_expire_entitlements()
    {
        using var client = _factory.CreateHttpsClient();
        var auth = await RegisterAsync(client, "webhook@example.com", "Webhook User");
        var activePayload = new
        {
            id = Guid.NewGuid().ToString("N"), type = "subscription_renewal", createdDate = DateTime.UtcNow, version = "2.1.0",
            data = new { userId = auth.UserId, productSku = "lumina_plus_monthly", platform = "android", originalPurchase = "purchase-verified-1", isSubscriptionActive = true, expirationDate = DateTime.UtcNow.AddMonths(1) }
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/subscriptions/iaphub/webhook", activePayload)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Auth-Token", "test-webhook-token");

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/subscriptions/iaphub/webhook", activePayload)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var active = await client.GetFromJsonAsync<SubscriptionStatusResponse>("/api/v1/subscriptions/status");
        Assert.True(active!.IsPremium);

        client.DefaultRequestHeaders.Authorization = null;
        var expiredPayload = new
        {
            id = Guid.NewGuid().ToString("N"), type = "subscription_expire", createdDate = DateTime.UtcNow, version = "2.1.0",
            data = new { userId = auth.UserId, productSku = "lumina_plus_monthly", platform = "android", originalPurchase = "purchase-verified-1", isSubscriptionActive = false, expirationDate = DateTime.UtcNow.AddMinutes(-1) }
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/subscriptions/iaphub/webhook", expiredPayload)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", auth.AccessToken);
        var expired = await client.GetFromJsonAsync<SubscriptionStatusResponse>("/api/v1/subscriptions/status");
        Assert.False(expired!.IsPremium); Assert.Equal("expired", expired.Status);
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string email, string displayName)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(email, "StrongPass!2026", displayName));
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }
}

public class FinanceApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Program validates that a SQL Server connection string exists before the test host swaps
        // the provider for EF Core InMemory. This placeholder is never used to open a connection.
        builder.UseSetting("ConnectionStrings:FinanceDatabase", "Server=localhost;Database=LuminaMoneyTests;Trusted_Connection=True;TrustServerCertificate=True");
        // These deterministic credentials are only for the isolated in-memory test host.
        builder.UseSetting("Jwt:Key", "CI-ONLY-NOT-A-SECRET-LUMINA-MONEY-TEST-SIGNING-KEY-0123456789");
        builder.UseSetting("Jwt:Issuer", "LuminaMoney.Tests");
        builder.UseSetting("Jwt:Audience", "LuminaMoney.Tests");
        builder.UseSetting("Identity:AccountCodePepper", "CI-ONLY-TEST-ACCOUNT-CODE-PEPPER-DO-NOT-USE-IN-PRODUCTION");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");
        builder.UseSetting("Subscriptions:Iaphub:WebhookToken", "test-webhook-token");
        builder.UseSetting("Subscriptions:AllowedProducts:0", "lumina_plus_monthly");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<FinanceDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<FinanceDbContext>>();
            services.RemoveAll<IAccountEmailSender>();
            var databaseName = $"lumina-api-{Guid.NewGuid():N}";
            services.AddDbContext<FinanceDbContext>(options => options.UseInMemoryDatabase(databaseName));
            services.AddSingleton<CapturingAccountEmailSender>();
            services.AddSingleton<IAccountEmailSender>(provider => provider.GetRequiredService<CapturingAccountEmailSender>());
        });
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
}

public sealed class CapturingAccountEmailSender : IAccountEmailSender
{
    public bool IsConfigured => true;
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> VerificationCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> PasswordResetCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task SendVerificationCodeAsync(string email, string displayName, string code, CancellationToken ct) { VerificationCodes[email] = code; return Task.CompletedTask; }
    public Task SendPasswordResetCodeAsync(string email, string displayName, string code, CancellationToken ct) { PasswordResetCodes[email] = code; return Task.CompletedTask; }
}
