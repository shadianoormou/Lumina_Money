using LuminaMoney.Api.Data;
using LuminaMoney.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LuminaMoney.Api.Controllers;
[ApiController, Authorize, Route("api/v1/finance")]
public sealed class FinanceController(FinanceDbContext db) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst("uid")!.Value);

    [HttpGet("sync")]
    public async Task<ActionResult<SyncSnapshot>> Sync(CancellationToken cancellationToken)
    {
        var uid = UserId;
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.UserId == uid).Select(x => new Account(x.Id, x.Name, (AccountType)x.Type, x.Currency, x.OpeningBalance, x.Color, x.IsArchived)).ToListAsync(cancellationToken);
        var categories = await db.Categories.AsNoTracking().Where(x => x.UserId == uid).OrderBy(x => x.SortOrder).Select(x => new BudgetCategory(x.Id, x.Name, x.Icon, x.Color, x.MonthlyTarget, x.SortOrder)).ToListAsync(cancellationToken);
        // A full snapshot must never be silently truncated. The mobile client replaces its
        // local cache from this response, so an arbitrary Take() here can delete older
        // customer history on the next successful sync.
        var transactionRows = await db.Transactions.AsNoTracking().Where(x => x.UserId == uid).OrderByDescending(x => x.Date).ToListAsync(cancellationToken);
        var transactionIds = transactionRows.Select(x => x.Id).ToList();
        var splitLookup = (await db.TransactionSplits.AsNoTracking().Where(x => x.UserId == uid && transactionIds.Contains(x.TransactionId)).ToListAsync(cancellationToken)).GroupBy(x => x.TransactionId).ToDictionary(x => x.Key, x => (IReadOnlyList<TransactionSplit>)x.Select(s => new TransactionSplit(s.Id, s.CategoryId, s.Amount, s.Note)).ToList());
        var transactions = transactionRows.Select(x => new MoneyTransaction(x.Id, x.AccountId, x.CategoryId, x.Date, x.Amount, (TransactionType)x.Type, x.Payee, x.Note, x.IsCleared, x.TransferAccountId, splitLookup.GetValueOrDefault(x.Id))).ToList();
        var goals = await db.Goals.AsNoTracking().Where(x => x.UserId == uid).Select(x => new SavingsGoal(x.Id, x.Name, x.TargetAmount, x.SavedAmount, x.TargetDate, x.Icon, x.Color)).ToListAsync(cancellationToken);
        var bills = await db.Bills.AsNoTracking().Where(x => x.UserId == uid).Select(x => new RecurringBill(x.Id, x.Name, x.Amount, x.NextDue, (RecurrenceFrequency)x.Frequency, x.CategoryId, x.IsSubscription)).ToListAsync(cancellationToken);
        var allocations = await db.BudgetAllocations.AsNoTracking().Where(x => x.UserId == uid).Select(x => new BudgetAllocation(x.Id, x.CategoryId, x.Year, x.Month, x.Assigned, x.RolledOver)).ToListAsync(cancellationToken);
        return Ok(new SyncSnapshot(accounts, categories, transactions, goals, bills, DateTime.UtcNow, allocations));
    }

    [HttpPut("transactions/{id:guid}")]
    public async Task<ActionResult> UpsertTransaction(Guid id, UpsertTransactionRequest request, CancellationToken ct)
    {
        if (id != request.Id || request.Amount <= 0 || request.Amount > 999_999_999_999m || string.IsNullOrWhiteSpace(request.Payee) || request.Payee.Trim().Length > 160 || request.Note is null || request.Note.Length > 500 || !Enum.IsDefined(request.Type)) return BadRequest(new ApiError("invalid_transaction", "A matching id, valid type, payee and positive amount are required."));
        var uid = UserId;
        if (!await db.Accounts.AnyAsync(x => x.Id == request.AccountId && x.UserId == uid, ct)) return BadRequest(new ApiError("invalid_account", "Account does not belong to this user."));
        if (request.Type == TransactionType.Transfer && (request.TransferAccountId is null || request.TransferAccountId == request.AccountId || !await db.Accounts.AnyAsync(x => x.Id == request.TransferAccountId && x.UserId == uid, ct))) return BadRequest(new ApiError("invalid_transfer", "Choose a different destination account owned by this user."));
        if (request.Type != TransactionType.Transfer && request.TransferAccountId is not null) return BadRequest(new ApiError("invalid_transfer", "Only transfers can have a destination account."));
        if (request.CategoryId is { } categoryId && !await db.Categories.AnyAsync(x => x.Id == categoryId && x.UserId == uid, ct)) return BadRequest(new ApiError("invalid_category", "Category does not belong to this user."));
        var splits = request.Splits?.ToList() ?? [];
        if (splits.Count > 0)
        {
            if (request.Type != TransactionType.Expense || request.CategoryId is not null || splits.Count < 2 || splits.Any(x => x.Amount <= 0 || x.Note is null || x.Note.Length > 240) || Math.Abs(splits.Sum(x => x.Amount) - request.Amount) >= 0.005m || splits.Select(x => x.Id).Distinct().Count() != splits.Count) return BadRequest(new ApiError("invalid_splits", "Expense splits need unique ids, at least two rows and a total matching the transaction amount."));
            var categoryIds = splits.Select(x => x.CategoryId).Distinct().ToList();
            if (await db.Categories.CountAsync(x => x.UserId == uid && categoryIds.Contains(x.Id), ct) != categoryIds.Count) return BadRequest(new ApiError("invalid_split_category", "Every split category must belong to this user."));
        }
        var entity = await db.Transactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct);
        if (entity is null) { entity = new TransactionEntity { Id = id, UserId = uid }; db.Transactions.Add(entity); }
        entity.AccountId = request.AccountId; entity.TransferAccountId = request.TransferAccountId; entity.CategoryId = request.Type == TransactionType.Transfer ? null : request.CategoryId; entity.Date = request.Date; entity.Amount = request.Amount;
        entity.Type = (int)request.Type; entity.Payee = request.Payee.Trim(); entity.Note = request.Note.Trim(); entity.IsCleared = request.IsCleared; entity.UpdatedUtc = DateTime.UtcNow;
        db.TransactionSplits.RemoveRange(db.TransactionSplits.Where(x => x.UserId == uid && x.TransactionId == id));
        if (splits.Count > 0) db.TransactionSplits.AddRange(splits.Select(x => new TransactionSplitEntity { Id = x.Id, UserId = uid, TransactionId = id, CategoryId = x.CategoryId, Amount = x.Amount, Note = x.Note.Trim() }));
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpDelete("transactions/{id:guid}")]
    public async Task<ActionResult> DeleteTransaction(Guid id, CancellationToken ct)
    {
        // DELETE is deliberately idempotent: a mobile client may retry after the first
        // response is lost even though SQL Server already committed the operation.
        var entity = await db.Transactions.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct); if (entity is null) return NoContent();
        db.TransactionSplits.RemoveRange(db.TransactionSplits.Where(x => x.UserId == UserId && x.TransactionId == id)); db.Transactions.Remove(entity); await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("accounts/{id:guid}")]
    public async Task<ActionResult> UpsertAccount(Guid id, Account value, CancellationToken ct)
    {
        if (id != value.Id || string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 120
            || string.IsNullOrWhiteSpace(value.Currency) || value.Currency.Length != 3 || !value.Currency.All(char.IsLetter)
            || Math.Abs(value.OpeningBalance) > MaximumMoney || !IsValidColor(value.Color) || !Enum.IsDefined(value.Type))
            return BadRequest(new ApiError("invalid_account", "Valid account data is required."));
        var entity = await db.Accounts.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (entity is null) { entity = new AccountEntity { Id = id, UserId = UserId }; db.Accounts.Add(entity); }
        entity.Name = value.Name.Trim(); entity.Type = (int)value.Type; entity.Currency = value.Currency.ToUpperInvariant(); entity.OpeningBalance = value.OpeningBalance; entity.Color = value.Color; entity.IsArchived = value.IsArchived; entity.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("categories/{id:guid}")]
    public async Task<ActionResult> UpsertCategory(Guid id, BudgetCategory value, CancellationToken ct)
    {
        if (id != value.Id || string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 80
            || string.IsNullOrWhiteSpace(value.Icon) || value.Icon.Length > 8 || !IsValidColor(value.Color)
            || value.MonthlyTarget < 0 || value.MonthlyTarget > MaximumMoney || value.SortOrder is < 0 or > 100_000)
            return BadRequest(new ApiError("invalid_category", "Valid category data is required."));
        var entity = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (entity is null) { entity = new CategoryEntity { Id = id, UserId = UserId }; db.Categories.Add(entity); }
        entity.Name = value.Name.Trim(); entity.Icon = value.Icon; entity.Color = value.Color; entity.MonthlyTarget = value.MonthlyTarget; entity.SortOrder = value.SortOrder; entity.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("goals/{id:guid}")]
    public async Task<ActionResult> UpsertGoal(Guid id, SavingsGoal value, CancellationToken ct)
    {
        if (id != value.Id || string.IsNullOrWhiteSpace(value.Name) || value.Name.Trim().Length > 120
            || string.IsNullOrWhiteSpace(value.Icon) || value.Icon.Length > 8 || !IsValidColor(value.Color)
            || value.TargetAmount <= 0 || value.TargetAmount > MaximumMoney || value.SavedAmount < 0 || value.SavedAmount > value.TargetAmount)
            return BadRequest(new ApiError("invalid_goal", "Valid goal data is required."));
        var entity = await db.Goals.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (entity is null) { entity = new GoalEntity { Id = id, UserId = UserId }; db.Goals.Add(entity); }
        entity.Name = value.Name.Trim(); entity.TargetAmount = value.TargetAmount; entity.SavedAmount = value.SavedAmount; entity.TargetDate = value.TargetDate; entity.Icon = value.Icon; entity.Color = value.Color; entity.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("bills/{id:guid}")]
    public async Task<ActionResult> UpsertBill(Guid id, RecurringBill value, CancellationToken ct)
    {
        if (id != value.Id || value.Amount <= 0 || value.Amount > MaximumMoney || string.IsNullOrWhiteSpace(value.Name)
            || value.Name.Trim().Length > 120 || !Enum.IsDefined(value.Frequency))
            return BadRequest(new ApiError("invalid_bill", "Valid recurring bill data is required."));
        if (value.CategoryId is { } categoryId && !await db.Categories.AnyAsync(x => x.Id == categoryId && x.UserId == UserId, ct))
            return BadRequest(new ApiError("invalid_category", "Category does not belong to this user."));
        var entity = await db.Bills.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (entity is null) { entity = new BillEntity { Id = id, UserId = UserId }; db.Bills.Add(entity); }
        entity.Name = value.Name.Trim(); entity.Amount = value.Amount; entity.NextDue = value.NextDue; entity.Frequency = (int)value.Frequency; entity.CategoryId = value.CategoryId; entity.IsSubscription = value.IsSubscription; entity.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("allocations/{id:guid}")]
    public async Task<ActionResult> UpsertAllocation(Guid id, BudgetAllocation value, CancellationToken ct)
    {
        if (id != value.Id || value.Assigned < 0 || value.Assigned > MaximumMoney || value.RolledOver < 0
            || value.RolledOver > MaximumMoney || value.Year is < 2000 or > 2200 || value.Month is < 1 or > 12)
            return BadRequest(new ApiError("invalid_allocation", "Valid allocation data is required."));
        var uid = UserId; if (!await db.Categories.AnyAsync(x => x.Id == value.CategoryId && x.UserId == uid, ct)) return BadRequest(new ApiError("invalid_category", "Category does not belong to this user."));
        var entity = await db.BudgetAllocations.SingleOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct);
        if (entity is null) { entity = new BudgetAllocationEntity { Id = id, UserId = uid }; db.BudgetAllocations.Add(entity); }
        entity.CategoryId = value.CategoryId; entity.Year = value.Year; entity.Month = value.Month; entity.Assigned = value.Assigned; entity.RolledOver = value.RolledOver; entity.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpDelete("categories/{id:guid}")]
    public async Task<ActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        var entity = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct); if (entity is null) return NoContent();
        if (await db.Transactions.AnyAsync(x => x.CategoryId == id && x.UserId == UserId, ct) || await db.TransactionSplits.AnyAsync(x => x.CategoryId == id && x.UserId == UserId, ct)) return Conflict(new ApiError("category_in_use", "Move or recategorise existing transactions before deleting this category."));
        db.BudgetAllocations.RemoveRange(db.BudgetAllocations.Where(x => x.CategoryId == id && x.UserId == UserId));
        db.Categories.Remove(entity); await db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpDelete("goals/{id:guid}")]
    public async Task<ActionResult> DeleteGoal(Guid id, CancellationToken ct) => await DeleteOwned(db.Goals, id, ct);

    [HttpDelete("bills/{id:guid}")]
    public async Task<ActionResult> DeleteBill(Guid id, CancellationToken ct) => await DeleteOwned(db.Bills, id, ct);

    private async Task<ActionResult> DeleteOwned<T>(DbSet<T> set, Guid id, CancellationToken ct) where T : UserOwnedEntity
    {
        var entity = await set.SingleOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct); if (entity is null) return NoContent();
        set.Remove(entity); await db.SaveChangesAsync(ct); return NoContent();
    }

    private const decimal MaximumMoney = 999_999_999_999m;

    private static bool IsValidColor(string? value)
    {
        if (value is null || value.Length is not (7 or 9) || value[0] != '#') return false;
        return value.AsSpan(1).ToString().All(Uri.IsHexDigit);
    }
}
