using LuminaMoney.Api.Data;
using LuminaMoney.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace LuminaMoney.Api.Controllers;

[ApiController, Authorize, Route("api/v1/account")]
public sealed class AccountController(FinanceDbContext db, UserManager<AppUser> users, ILogger<AccountController> logger) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("uid")!.Value);

    [HttpGet("export")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<AccountExportResponse>> Export(CancellationToken ct)
    {
        var uid = UserId;
        var user = await users.FindByIdAsync(uid.ToString());
        if (user is null) return Unauthorized(new ApiError("account_not_found", "This account no longer exists."));

        var accounts = await db.Accounts.AsNoTracking().Where(x => x.UserId == uid)
            .Select(x => new Account(x.Id, x.Name, (AccountType)x.Type, x.Currency, x.OpeningBalance, x.Color, x.IsArchived)).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().Where(x => x.UserId == uid).OrderBy(x => x.SortOrder)
            .Select(x => new BudgetCategory(x.Id, x.Name, x.Icon, x.Color, x.MonthlyTarget, x.SortOrder)).ToListAsync(ct);
        var rows = await db.Transactions.AsNoTracking().Where(x => x.UserId == uid).OrderByDescending(x => x.Date).ToListAsync(ct);
        var transactionIds = rows.Select(x => x.Id).ToList();
        var splitLookup = (await db.TransactionSplits.AsNoTracking().Where(x => x.UserId == uid && transactionIds.Contains(x.TransactionId)).ToListAsync(ct))
            .GroupBy(x => x.TransactionId).ToDictionary(x => x.Key, x => (IReadOnlyList<TransactionSplit>)x.Select(s => new TransactionSplit(s.Id, s.CategoryId, s.Amount, s.Note)).ToList());
        var transactions = rows.Select(x => new MoneyTransaction(x.Id, x.AccountId, x.CategoryId, x.Date, x.Amount, (TransactionType)x.Type, x.Payee, x.Note, x.IsCleared, x.TransferAccountId, splitLookup.GetValueOrDefault(x.Id))).ToList();
        var goals = await db.Goals.AsNoTracking().Where(x => x.UserId == uid)
            .Select(x => new SavingsGoal(x.Id, x.Name, x.TargetAmount, x.SavedAmount, x.TargetDate, x.Icon, x.Color)).ToListAsync(ct);
        var bills = await db.Bills.AsNoTracking().Where(x => x.UserId == uid)
            .Select(x => new RecurringBill(x.Id, x.Name, x.Amount, x.NextDue, (RecurrenceFrequency)x.Frequency, x.CategoryId, x.IsSubscription)).ToListAsync(ct);
        var allocations = await db.BudgetAllocations.AsNoTracking().Where(x => x.UserId == uid)
            .Select(x => new BudgetAllocation(x.Id, x.CategoryId, x.Year, x.Month, x.Assigned, x.RolledOver)).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var snapshot = new SyncSnapshot(accounts, categories, transactions, goals, bills, now, allocations);
        return Ok(new AccountExportResponse(now, new(uid.ToString(), user.DisplayName, user.Email ?? "", user.CreatedUtc), snapshot));
    }

    [HttpDelete]
    public async Task<ActionResult> Delete(DeleteAccountRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.Confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
            return BadRequest(new ApiError("confirmation_required", "Type DELETE to confirm permanent account deletion."));
        if (string.IsNullOrEmpty(request.Password))
            return BadRequest(new ApiError("password_required", "Enter your password to delete the account."));

        var uid = UserId;
        var user = await users.FindByIdAsync(uid.ToString());
        if (user is null) return NoContent();
        if (!await users.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new ApiError("invalid_password", "The password is incorrect."));

        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational()) transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.TransactionSplits.RemoveRange(db.TransactionSplits.Where(x => x.UserId == uid));
            db.Transactions.RemoveRange(db.Transactions.Where(x => x.UserId == uid));
            db.BudgetAllocations.RemoveRange(db.BudgetAllocations.Where(x => x.UserId == uid));
            db.Bills.RemoveRange(db.Bills.Where(x => x.UserId == uid));
            db.Goals.RemoveRange(db.Goals.Where(x => x.UserId == uid));
            db.Categories.RemoveRange(db.Categories.Where(x => x.UserId == uid));
            db.Accounts.RemoveRange(db.Accounts.Where(x => x.UserId == uid));
            db.BankConnections.RemoveRange(db.BankConnections.Where(x => x.UserId == uid));
            db.SubscriptionEntitlements.RemoveRange(db.SubscriptionEntitlements.Where(x => x.UserId == uid));
            db.AccountActionChallenges.RemoveRange(db.AccountActionChallenges.Where(x => x.UserId == uid));
            db.RefreshTokens.RemoveRange(db.RefreshTokens.Where(x => x.UserId == uid));
            await db.SaveChangesAsync(ct);

            var result = await users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                if (transaction is not null) await transaction.RollbackAsync(ct);
                logger.LogError("Account deletion failed for user {UserId}: {Codes}", uid, string.Join(',', result.Errors.Select(x => x.Code)));
                return Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Account deletion could not be completed.");
            }
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            throw;
        }
        finally { if (transaction is not null) await transaction.DisposeAsync(); }

        logger.LogInformation("Account {UserId} and all owned finance data were deleted", uid);
        return NoContent();
    }
}
