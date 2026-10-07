using LuminaMoney.Api.Data;
using LuminaMoney.Api.Services;
using LuminaMoney.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LuminaMoney.Api.Controllers;

[ApiController, Authorize, Route("api/v1/bank-connections")]
public sealed class BankConnectionsController(FinanceDbContext db, UserManager<AppUser> users, IBankConnectionProvider provider) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("uid")!.Value);
    private string? UserIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet("provider")]
    public ActionResult<BankProviderStatusResponse> ProviderStatus() => Ok(new BankProviderStatusResponse(
        provider.Name, provider.IsConfigured, provider.Environment, ["UK Open Banking", "Accounts", "Balances", "Transactions", "Recurring consent"]));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BankConnectionResponse>>> List(CancellationToken ct)
    {
        var rows = await db.BankConnections.AsNoTracking().Where(x => x.UserId == UserId).OrderByDescending(x => x.UpdatedUtc).ToListAsync(ct);
        return Ok(rows.Select(ToResponse).ToList());
    }

    [HttpPost("truelayer/start")]
    public async Task<ActionResult<StartBankConnectionResponse>> Start(CancellationToken ct)
    {
        if (!provider.IsConfigured)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiError("bank_provider_unavailable", "TrueLayer credentials and a registered HTTPS return URI are required on this environment."));
        var user = await users.FindByIdAsync(UserId.ToString());
        if (user is null) return Unauthorized();
        var started = await provider.StartAsync(UserId, user.DisplayName, user.Email ?? "", UserIp, ct);
        var entity = new BankConnectionEntity
        {
            Id = Guid.NewGuid(), UserId = UserId, Provider = "truelayer", ExternalConnectionId = started.ExternalConnectionId,
            Status = "pending_authorisation", Environment = started.Environment, ConsentExpiresUtc = started.ConsentExpiresUtc
        };
        db.BankConnections.Add(entity); await db.SaveChangesAsync(ct);
        return Ok(new StartBankConnectionResponse(entity.Id, started.AuthorizationUri, started.ConsentExpiresUtc));
    }

    [HttpPost("{id:guid}/refresh")]
    public async Task<ActionResult<BankConnectionResponse>> Refresh(Guid id, CancellationToken ct)
    {
        var row = await db.BankConnections.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (row is null) return NotFound();
        if (!provider.IsConfigured) return StatusCode(StatusCodes.Status503ServiceUnavailable, new ApiError("bank_provider_unavailable", "TrueLayer is not configured."));
        try
        {
            var active = await provider.IsAuthorisedAsync(row.ExternalConnectionId, UserIp, ct);
            if (!active)
            {
                row.Status = "authorisation_required"; row.UpdatedUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(ct); return Ok(ToResponse(row));
            }

            var import = await provider.ImportAsync(row.ExternalConnectionId, UserIp, ct);
            await UpsertImportAsync(row, import, ct);
            row.Status = import.IsPending ? "sync_pending" : "active";
            row.LastSyncedUtc = DateTime.UtcNow; row.UpdatedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return import.IsPending ? Accepted(ToResponse(row)) : Ok(ToResponse(row));
        }
        catch (HttpRequestException)
        {
            row.Status = "provider_unavailable"; row.UpdatedUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct);
            return StatusCode(StatusCodes.Status502BadGateway, new ApiError("bank_provider_error", "TrueLayer or the selected bank is temporarily unavailable. No local records were removed."));
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var row = await db.BankConnections.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (row is null) return NoContent();
        db.BankConnections.Remove(row); await db.SaveChangesAsync(ct); return NoContent();
    }

    private async Task UpsertImportAsync(BankConnectionEntity connection, BankDataImport import, CancellationToken ct)
    {
        const string source = "truelayer";
        var prefix = $"{connection.ExternalConnectionId}:";
        var accounts = await db.Accounts.Where(x => x.UserId == UserId && x.Source == source && x.ExternalSourceId != null && x.ExternalSourceId.StartsWith(prefix)).ToListAsync(ct);
        var accountLookup = accounts.ToDictionary(x => x.ExternalSourceId!, StringComparer.Ordinal);
        foreach (var item in import.Accounts.Where(x => x.ExternalId.Length <= 230))
        {
            var sourceId = prefix + item.ExternalId;
            if (!accountLookup.TryGetValue(sourceId, out var account))
            {
                account = new AccountEntity { Id = Guid.NewGuid(), UserId = UserId, Source = source, ExternalSourceId = sourceId, Color = "#2F72F4" };
                db.Accounts.Add(account); accountLookup[sourceId] = account;
            }
            account.Name = Truncate($"TrueLayer {FriendlyAccountType(item.Type, item.AccountType)}", 120);
            account.Type = (int)MapAccountType(item.Type, item.AccountType); account.Currency = item.Currency;
            account.IsArchived = false; account.UpdatedUtc = DateTime.UtcNow;
        }

        var transactions = await db.Transactions.Where(x => x.UserId == UserId && x.Source == source && x.ExternalSourceId != null && x.ExternalSourceId.StartsWith(prefix)).ToListAsync(ct);
        var transactionLookup = transactions.ToDictionary(x => x.ExternalSourceId!, StringComparer.Ordinal);
        foreach (var item in import.Transactions.Where(x => x.ExternalId.Length <= 230 && x.AccountExternalId.Length <= 230))
        {
            var accountSourceId = prefix + item.AccountExternalId;
            if (!accountLookup.TryGetValue(accountSourceId, out var account)) continue;
            var sourceId = $"{accountSourceId}:{item.ExternalId}";
            if (!transactionLookup.TryGetValue(sourceId, out var transaction))
            {
                transaction = new TransactionEntity { Id = Guid.NewGuid(), UserId = UserId, Source = source, ExternalSourceId = sourceId };
                db.Transactions.Add(transaction); transactionLookup[sourceId] = transaction;
            }
            transaction.AccountId = account.Id; transaction.TransferAccountId = null; transaction.Date = item.Timestamp;
            transaction.Amount = Math.Abs((decimal)item.AmountInMinor) / 100m;
            transaction.Type = (int)(item.AmountInMinor < 0 ? TransactionType.Expense : TransactionType.Income);
            transaction.Payee = Truncate(string.IsNullOrWhiteSpace(item.MerchantName) ? item.Description : item.MerchantName, 160);
            if (string.IsNullOrWhiteSpace(transaction.Note) && !string.IsNullOrWhiteSpace(item.CategoryName))
                transaction.Note = Truncate($"TrueLayer category: {item.CategoryName}", 500);
            transaction.IsCleared = string.Equals(item.Status, "settled", StringComparison.OrdinalIgnoreCase);
            transaction.UpdatedUtc = DateTime.UtcNow;
        }
        connection.ImportedAccountCount = accountLookup.Count;
        connection.ImportedTransactionCount = transactionLookup.Count;
    }

    private static AccountType MapAccountType(string type, string accountType) =>
        type.Equals("card", StringComparison.OrdinalIgnoreCase) ? AccountType.CreditCard :
        accountType.Equals("savings", StringComparison.OrdinalIgnoreCase) ? AccountType.Savings : AccountType.Current;

    private static string FriendlyAccountType(string type, string accountType) =>
        type.Equals("card", StringComparison.OrdinalIgnoreCase) ? "card" : accountType.Equals("savings", StringComparison.OrdinalIgnoreCase) ? "savings" : "current account";

    private static string Truncate(string? value, int length) => string.IsNullOrWhiteSpace(value) ? "Bank transaction" : value.Trim()[..Math.Min(value.Trim().Length, length)];

    private static BankConnectionResponse ToResponse(BankConnectionEntity row) => new(row.Id, row.Provider, row.Status, row.Environment,
        row.ConsentExpiresUtc, row.LastSyncedUtc, row.ImportedAccountCount, row.ImportedTransactionCount);
}
