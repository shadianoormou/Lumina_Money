using LuminaMoney.Core;
using SQLite;
using System.Text.Json;

namespace LuminaMoney.App.Services;

public sealed class FinanceDatabase(SessionService session, LocalDataProtector protector)
{
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private SQLiteAsyncConnection? _db;
    private string? _profileId;
    private byte[] _key = [];

    public async Task InitializeAsync() => _ = await GetDatabaseAsync();

    public async Task<FinanceData> LoadAsync()
    {
        var db = await GetDatabaseAsync();
        var accounts = (await db.Table<AccountRow>().ToListAsync()).Select(ToModel).Select(x => x with { Color = NormalizeColor(x.Color) }).ToList();
        var categories = (await db.Table<CategoryRow>().ToListAsync()).Select(ToModel).Select(x => x with { Color = NormalizeColor(x.Color) }).ToList();
        var transactions = (await db.Table<TransactionRow>().ToListAsync()).Select(ToModel).ToList();
        var goals = (await db.Table<GoalRow>().ToListAsync()).Select(ToModel).Select(x => x with { Color = NormalizeColor(x.Color) }).ToList();
        var bills = (await db.Table<BillRow>().ToListAsync()).Select(ToModel).ToList();
        var allocations = (await db.Table<AllocationRow>().ToListAsync()).Select(x => x.Model).ToList();
        return new(accounts, categories, transactions, goals, bills, allocations);
    }

    public async Task SaveTransactionAsync(MoneyTransaction item)
    {
        var db = await GetDatabaseAsync(); var row = Protect(new TransactionRow(item)); var request = new UpsertTransactionRequest(item.Id, item.AccountId, item.CategoryId, item.Date, item.Amount, item.Type, item.Payee, item.Note, item.IsCleared, item.TransferAccountId, item.Splits);
        var operation = Pending("PUT", $"finance/transactions/{item.Id}", JsonSerializer.Serialize(request));
        await db.RunInTransactionAsync(c => { c.InsertOrReplace(row); c.Insert(operation); });
    }

    public async Task SaveTransactionsAsync(IEnumerable<MoneyTransaction> items)
    {
        var db = await GetDatabaseAsync(); var values = items.Select(item => (Row: Protect(new TransactionRow(item)), Operation: Pending("PUT", $"finance/transactions/{item.Id}", JsonSerializer.Serialize(new UpsertTransactionRequest(item.Id, item.AccountId, item.CategoryId, item.Date, item.Amount, item.Type, item.Payee, item.Note, item.IsCleared, item.TransferAccountId, item.Splits))))).ToList();
        await db.RunInTransactionAsync(c => { foreach (var value in values) { c.InsertOrReplace(value.Row); c.Insert(value.Operation); } });
    }

    public async Task DeleteTransactionAsync(Guid id) { var db = await GetDatabaseAsync(); await db.RunInTransactionAsync(c => { c.Delete<TransactionRow>(id.ToString()); c.Insert(Pending("DELETE", $"finance/transactions/{id}", null)); }); }
    public async Task SaveAccountAsync(Account item) { var db = await GetDatabaseAsync(); var row = Protect(new AccountRow(item)); await db.RunInTransactionAsync(c => { c.InsertOrReplace(row); c.Insert(Pending("PUT", $"finance/accounts/{item.Id}", JsonSerializer.Serialize(item))); }); }
    public async Task SaveCategoryAsync(BudgetCategory item) { var db = await GetDatabaseAsync(); var row = Protect(new CategoryRow(item)); await db.RunInTransactionAsync(c => { c.InsertOrReplace(row); c.Insert(Pending("PUT", $"finance/categories/{item.Id}", JsonSerializer.Serialize(item))); }); }
    public async Task SaveGoalAsync(SavingsGoal item) { var db = await GetDatabaseAsync(); var row = Protect(new GoalRow(item)); await db.RunInTransactionAsync(c => { c.InsertOrReplace(row); c.Insert(Pending("PUT", $"finance/goals/{item.Id}", JsonSerializer.Serialize(item))); }); }
    public async Task SaveBillAsync(RecurringBill item) { var db = await GetDatabaseAsync(); var row = Protect(new BillRow(item)); await db.RunInTransactionAsync(c => { c.InsertOrReplace(row); c.Insert(Pending("PUT", $"finance/bills/{item.Id}", JsonSerializer.Serialize(item))); }); }
    public async Task SaveAllocationAsync(BudgetAllocation item) { var db = await GetDatabaseAsync(); await db.RunInTransactionAsync(c => { c.InsertOrReplace(new AllocationRow(item)); c.Insert(Pending("PUT", $"finance/allocations/{item.Id}", JsonSerializer.Serialize(item))); }); }
    public async Task DeleteCategoryAsync(Guid id) { var db = await GetDatabaseAsync(); await db.RunInTransactionAsync(c => { foreach (var row in c.Table<AllocationRow>().Where(x => x.CategoryId == id.ToString()).ToList()) c.Delete(row); c.Delete<CategoryRow>(id.ToString()); c.Insert(Pending("DELETE", $"finance/categories/{id}", null)); }); }
    public Task DeleteGoalAsync(Guid id) => DeleteAsync<GoalRow>(id, "goals");
    public Task DeleteBillAsync(Guid id) => DeleteAsync<BillRow>(id, "bills");

    public async Task<IReadOnlyList<PendingOperationRow>> PendingAsync()
    {
        var db = await GetDatabaseAsync(); var rows = await db.Table<PendingOperationRow>().OrderBy(x => x.Sequence).ToListAsync();
        return rows.Select(x => new PendingOperationRow { Sequence = x.Sequence, Method = x.Method, Path = x.Path, Payload = x.Payload is null ? null : protector.Unprotect(x.Payload, _key), CreatedUtc = x.CreatedUtc }).ToList();
    }

    public async Task CompletePendingAsync(int sequence) { var db = await GetDatabaseAsync(); await db.DeleteAsync<PendingOperationRow>(sequence); }

    public async Task ReplaceFromServerAsync(SyncSnapshot snapshot)
    {
        var db = await GetDatabaseAsync(); var accounts = snapshot.Accounts.Select(x => Protect(new AccountRow(x))).ToList(); var categories = snapshot.Categories.Select(x => Protect(new CategoryRow(x))).ToList();
        var transactions = snapshot.Transactions.Select(x => Protect(new TransactionRow(x))).ToList(); var goals = snapshot.Goals.Select(x => Protect(new GoalRow(x))).ToList(); var bills = snapshot.Bills.Select(x => Protect(new BillRow(x))).ToList();
        await db.RunInTransactionAsync(c =>
        {
            c.DeleteAll<AccountRow>(); c.DeleteAll<CategoryRow>(); c.DeleteAll<TransactionRow>(); c.DeleteAll<GoalRow>(); c.DeleteAll<BillRow>(); c.DeleteAll<AllocationRow>();
            c.InsertAll(accounts); c.InsertAll(categories); c.InsertAll(transactions); c.InsertAll(goals); c.InsertAll(bills); c.InsertAll((snapshot.Allocations ?? []).Select(x => new AllocationRow(x)));
        });
    }

    public async Task EraseCurrentProfileAsync()
    {
        await _connectionGate.WaitAsync();
        try
        {
            var db = _db; var path = db?.DatabasePath;
            if (db is not null) await db.CloseAsync();
            _db = null; _profileId = null; _key = [];
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
        }
        finally { _connectionGate.Release(); }
    }

    private async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        var profile = await session.GetProfileIdAsync();
        if (_db is not null && _profileId == profile) return _db;
        await _connectionGate.WaitAsync();
        try
        {
            if (_db is not null && _profileId == profile) return _db;
            if (_db is not null) await _db.CloseAsync();
            _profileId = profile; _key = await protector.GetOrCreateKeyAsync(profile);
            var path = Path.Combine(FileSystem.AppDataDirectory, $"lumina-{profile}.db3");
            _db = new(path, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);
            await _db.CreateTableAsync<AccountRow>(); await _db.CreateTableAsync<CategoryRow>(); await _db.CreateTableAsync<TransactionRow>(); await _db.CreateTableAsync<GoalRow>(); await _db.CreateTableAsync<BillRow>(); await _db.CreateTableAsync<AllocationRow>(); await _db.CreateTableAsync<PendingOperationRow>();
            await MigrateSensitiveTextAsync(_db);
            if (await _db.Table<AccountRow>().CountAsync() == 0) await SeedAsync(_db);
            return _db;
        }
        finally { _connectionGate.Release(); }
    }

    private async Task MigrateSensitiveTextAsync(SQLiteAsyncConnection db)
    {
        foreach (var row in await db.Table<AccountRow>().ToListAsync()) if (!protector.IsProtected(row.Name)) { row.Name = protector.Protect(row.Name, _key); await db.UpdateAsync(row); }
        foreach (var row in await db.Table<CategoryRow>().ToListAsync()) if (!protector.IsProtected(row.Name)) { row.Name = protector.Protect(row.Name, _key); await db.UpdateAsync(row); }
        foreach (var row in await db.Table<TransactionRow>().ToListAsync()) if (!protector.IsProtected(row.Payee) || !protector.IsProtected(row.Note) || !protector.IsProtected(row.SplitsJson)) { row.Payee = protector.Protect(row.Payee, _key); row.Note = protector.Protect(row.Note, _key); row.SplitsJson = protector.Protect(string.IsNullOrWhiteSpace(row.SplitsJson) ? "[]" : row.SplitsJson, _key); await db.UpdateAsync(row); }
        foreach (var row in await db.Table<GoalRow>().ToListAsync()) if (!protector.IsProtected(row.Name)) { row.Name = protector.Protect(row.Name, _key); await db.UpdateAsync(row); }
        foreach (var row in await db.Table<BillRow>().ToListAsync()) if (!protector.IsProtected(row.Name)) { row.Name = protector.Protect(row.Name, _key); await db.UpdateAsync(row); }
        foreach (var row in await db.Table<PendingOperationRow>().ToListAsync()) if (row.Payload is not null && !protector.IsProtected(row.Payload)) { row.Payload = protector.Protect(row.Payload, _key); await db.UpdateAsync(row); }
    }

    private async Task SeedAsync(SQLiteAsyncConnection db)
    {
        var main = new Account(Guid.NewGuid(), "Everyday account", AccountType.Current, "GBP", 0, "#5797F4");
        var categories = new[] { new BudgetCategory(Guid.NewGuid(), "Home", "⌂", "#3E79DB", 1200, 1), new BudgetCategory(Guid.NewGuid(), "Food", "◉", "#5797F4", 480, 2), new BudgetCategory(Guid.NewGuid(), "Transport", "↗", "#7AB5FF", 240, 3), new BudgetCategory(Guid.NewGuid(), "Lifestyle", "✦", "#7890EE", 320, 4) };
        await db.InsertAsync(Protect(new AccountRow(main))); await db.InsertAllAsync(categories.Select(x => Protect(new CategoryRow(x))));
    }

    private PendingOperationRow Pending(string method, string path, string? payload) => new(method, path, payload is null ? null : protector.Protect(payload, _key));
    private AccountRow Protect(AccountRow row) { row.Name = protector.Protect(row.Name, _key); return row; }
    private CategoryRow Protect(CategoryRow row) { row.Name = protector.Protect(row.Name, _key); return row; }
    private TransactionRow Protect(TransactionRow row) { row.Payee = protector.Protect(row.Payee, _key); row.Note = protector.Protect(row.Note, _key); row.SplitsJson = protector.Protect(row.SplitsJson, _key); return row; }
    private GoalRow Protect(GoalRow row) { row.Name = protector.Protect(row.Name, _key); return row; }
    private BillRow Protect(BillRow row) { row.Name = protector.Protect(row.Name, _key); return row; }
    private Account ToModel(AccountRow x) => new(Guid.Parse(x.Id), protector.Unprotect(x.Name, _key), (AccountType)x.Type, x.Currency, x.OpeningBalance, x.Color, x.IsArchived);
    private BudgetCategory ToModel(CategoryRow x) => new(Guid.Parse(x.Id), protector.Unprotect(x.Name, _key), x.Icon, x.Color, x.MonthlyTarget, x.SortOrder);
    private MoneyTransaction ToModel(TransactionRow x) => new(Guid.Parse(x.Id), Guid.Parse(x.AccountId), string.IsNullOrEmpty(x.CategoryId) ? null : Guid.Parse(x.CategoryId), x.Date, x.Amount, (TransactionType)x.Type, protector.Unprotect(x.Payee, _key), protector.Unprotect(x.Note, _key), x.IsCleared, string.IsNullOrEmpty(x.TransferAccountId) ? null : Guid.Parse(x.TransferAccountId), JsonSerializer.Deserialize<IReadOnlyList<TransactionSplit>>(protector.Unprotect(x.SplitsJson, _key)) ?? []);
    private SavingsGoal ToModel(GoalRow x) => new(Guid.Parse(x.Id), protector.Unprotect(x.Name, _key), x.TargetAmount, x.SavedAmount, x.TargetDate, x.Icon, x.Color);
    private RecurringBill ToModel(BillRow x) => new(Guid.Parse(x.Id), protector.Unprotect(x.Name, _key), x.Amount, x.NextDue, (RecurrenceFrequency)x.Frequency, string.IsNullOrEmpty(x.CategoryId) ? null : Guid.Parse(x.CategoryId), x.IsSubscription);

    private async Task DeleteAsync<TRow>(Guid id, string resource) where TRow : new() { var db = await GetDatabaseAsync(); await db.RunInTransactionAsync(c => { c.Delete<TRow>(id.ToString()); c.Insert(Pending("DELETE", $"finance/{resource}/{id}", null)); }); }
    private static string NormalizeColor(string color) => color.ToUpperInvariant() switch { "#6C5CE7" => "#5797F4", "#18B889" => "#62A9D6", "#FF9F43" => "#7AB5FF", "#2D9CDB" => "#3E79DB", "#E667A0" => "#7890EE", "#10B981" => "#62A9D6", "#2F72F4" => "#5797F4", "#F2A93B" => "#7AB5FF", "#20A4B8" => "#7890EE", _ => color };
}

public sealed record FinanceData(IReadOnlyList<Account> Accounts, IReadOnlyList<BudgetCategory> Categories, IReadOnlyList<MoneyTransaction> Transactions, IReadOnlyList<SavingsGoal> Goals, IReadOnlyList<RecurringBill> Bills, IReadOnlyList<BudgetAllocation> Allocations);
public abstract class Row { [PrimaryKey] public string Id { get; set; } = ""; }
[Table("PendingOperations")]
public sealed class PendingOperationRow { public PendingOperationRow() { } public PendingOperationRow(string method, string path, string? payload) { Method = method; Path = path; Payload = payload; CreatedUtc = DateTime.UtcNow; } [PrimaryKey, AutoIncrement] public int Sequence { get; set; } public string Method { get; set; } = ""; public string Path { get; set; } = ""; public string? Payload { get; set; } public DateTime CreatedUtc { get; set; } }
public sealed class AccountRow : Row { public AccountRow() { } public AccountRow(Account x) { Id = x.Id.ToString(); Name = x.Name; Type = (int)x.Type; Currency = x.Currency; OpeningBalance = x.OpeningBalance; Color = x.Color; IsArchived = x.IsArchived; } public string Name { get; set; } = ""; public int Type { get; set; } public string Currency { get; set; } = "GBP"; public decimal OpeningBalance { get; set; } public string Color { get; set; } = ""; public bool IsArchived { get; set; } }
public sealed class CategoryRow : Row { public CategoryRow() { } public CategoryRow(BudgetCategory x) { Id = x.Id.ToString(); Name = x.Name; Icon = x.Icon; Color = x.Color; MonthlyTarget = x.MonthlyTarget; SortOrder = x.SortOrder; } public string Name { get; set; } = ""; public string Icon { get; set; } = ""; public string Color { get; set; } = ""; public decimal MonthlyTarget { get; set; } public int SortOrder { get; set; } }
public sealed class TransactionRow : Row { public TransactionRow() { } public TransactionRow(MoneyTransaction x) { Id = x.Id.ToString(); AccountId = x.AccountId.ToString(); TransferAccountId = x.TransferAccountId?.ToString(); CategoryId = x.CategoryId?.ToString(); Date = x.Date; Amount = x.Amount; Type = (int)x.Type; Payee = x.Payee; Note = x.Note; IsCleared = x.IsCleared; SplitsJson = JsonSerializer.Serialize(x.Splits ?? []); } public string AccountId { get; set; } = ""; public string? TransferAccountId { get; set; } public string? CategoryId { get; set; } public DateTime Date { get; set; } public decimal Amount { get; set; } public int Type { get; set; } public string Payee { get; set; } = ""; public string Note { get; set; } = ""; public bool IsCleared { get; set; } public string SplitsJson { get; set; } = "[]"; }
public sealed class GoalRow : Row { public GoalRow() { } public GoalRow(SavingsGoal x) { Id = x.Id.ToString(); Name = x.Name; TargetAmount = x.TargetAmount; SavedAmount = x.SavedAmount; TargetDate = x.TargetDate; Icon = x.Icon; Color = x.Color; } public string Name { get; set; } = ""; public decimal TargetAmount { get; set; } public decimal SavedAmount { get; set; } public DateTime TargetDate { get; set; } public string Icon { get; set; } = ""; public string Color { get; set; } = ""; }
public sealed class BillRow : Row { public BillRow() { } public BillRow(RecurringBill x) { Id = x.Id.ToString(); Name = x.Name; Amount = x.Amount; NextDue = x.NextDue; Frequency = (int)x.Frequency; CategoryId = x.CategoryId?.ToString(); IsSubscription = x.IsSubscription; } public string Name { get; set; } = ""; public decimal Amount { get; set; } public DateTime NextDue { get; set; } public int Frequency { get; set; } public string? CategoryId { get; set; } public bool IsSubscription { get; set; } }
public sealed class AllocationRow : Row { public AllocationRow() { } public AllocationRow(BudgetAllocation x) { Id = x.Id.ToString(); CategoryId = x.CategoryId.ToString(); Year = x.Year; Month = x.Month; Assigned = x.Assigned; RolledOver = x.RolledOver; } public string CategoryId { get; set; } = ""; public int Year { get; set; } public int Month { get; set; } public decimal Assigned { get; set; } public decimal RolledOver { get; set; } public BudgetAllocation Model => new(Guid.Parse(Id), Guid.Parse(CategoryId), Year, Month, Assigned, RolledOver); }
