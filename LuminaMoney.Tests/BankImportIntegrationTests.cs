using System.Net.Http.Headers;
using System.Net.Http.Json;
using LuminaMoney.Api.Services;
using LuminaMoney.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LuminaMoney.Tests;

public sealed class BankImportIntegrationTests : IClassFixture<BankingApiFactory>
{
    private readonly BankingApiFactory _factory;
    public BankImportIntegrationTests(BankingApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Authorised_bank_refresh_imports_and_deduplicates_accounts_and_transactions()
    {
        using var client = _factory.CreateHttpsClient();
        var registration = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("bank-import@example.com", "StrongPass!2026", "Bank Import"));
        registration.EnsureSuccessStatusCode();
        var auth = (await registration.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var start = await client.PostAsync("/api/v1/bank-connections/truelayer/start", null);
        start.EnsureSuccessStatusCode();
        var connection = (await start.Content.ReadFromJsonAsync<StartBankConnectionResponse>())!;
        var first = await client.PostAsync($"/api/v1/bank-connections/{connection.ConnectionId}/refresh", null);
        first.EnsureSuccessStatusCode();
        var firstStatus = (await first.Content.ReadFromJsonAsync<BankConnectionResponse>())!;
        var second = await client.PostAsync($"/api/v1/bank-connections/{connection.ConnectionId}/refresh", null);
        second.EnsureSuccessStatusCode();
        var snapshot = await client.GetFromJsonAsync<SyncSnapshot>("/api/v1/finance/sync");

        Assert.Equal("active", firstStatus.Status);
        Assert.Equal(1, firstStatus.ImportedAccounts);
        Assert.Equal(2, firstStatus.ImportedTransactions);
        Assert.Equal(2, snapshot!.Accounts.Count);
        Assert.Equal(2, snapshot.Transactions.Count);
        Assert.Equal(42m, snapshot.Transactions.Single(x => x.Type == TransactionType.Expense).Amount);
        Assert.Equal("Ankh-Morpork Post Office", snapshot.Transactions.Single(x => x.Type == TransactionType.Expense).Payee);
    }
}

public sealed class BankingApiFactory : FinanceApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IBankConnectionProvider>();
            services.AddSingleton<IBankConnectionProvider, FakeBankConnectionProvider>();
        });
    }
}

public sealed class FakeBankConnectionProvider : IBankConnectionProvider
{
    public string Name => "TrueLayer";
    public string Environment => "sandbox";
    public bool IsConfigured => true;
    public Task<BankConnectionStart> StartAsync(Guid userId, string displayName, string email, string? userIp, CancellationToken ct) =>
        Task.FromResult(new BankConnectionStart(Guid.NewGuid().ToString(), "https://example.test/authorise", "sandbox", DateTime.UtcNow.AddDays(90)));
    public Task<bool> IsAuthorisedAsync(string externalConnectionId, string? userIp, CancellationToken ct) => Task.FromResult(true);
    public Task<BankDataImport> ImportAsync(string externalConnectionId, string? userIp, CancellationToken ct) => Task.FromResult(new BankDataImport(
        [new ImportedBankAccount("account-1", "account", "current", "GBP", ["Test User"])],
        [
            new ImportedBankTransaction("transaction-1", "account-1", new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc), "POST OFFICE", "GBP", -4200, "settled", "Ankh-Morpork Post Office", "Shopping"),
            new ImportedBankTransaction("transaction-2", "account-1", new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc), "SALARY", "GBP", 250000, "settled", null, "Income")
        ], false));
}
