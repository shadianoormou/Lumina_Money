using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LuminaMoney.App.Services;
using LuminaMoney.Core;

namespace LuminaMoney.App.ViewModels;

public partial class AppViewModel : ObservableObject
{
    private readonly FinanceDatabase _database;
    private readonly FinanceSyncService _sync;
    private readonly SessionService _session;
    private readonly BillReminderService _reminders;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private FinanceData? _data;
    private DateTime _lastLoadedUtc;
    private bool _syncAttempted;
    private Guid? _editingTransactionId;
    private Guid? _editingAccountId;
    private Guid? _editingCategoryId;
    private Guid? _editingGoalId;
    private Guid? _editingBillId;
    private Guid? _fundingGoalId;
    private CancellationTokenSource? _statusDismissal;
    private CancellationTokenSource? _searchDebounce;
    private IReadOnlyList<MoneyTransaction> _filteredTransactions = [];
    private int _transactionsShown;
    private const int TransactionPageSize = 50;

    public AppViewModel(FinanceDatabase database, FinanceSyncService sync, SessionService session, BillReminderService reminders)
    {
        _database = database; _sync = sync; _session = session; _reminders = reminders;
        TransactionFilters = [new("All", true), new("Income"), new("Expenses"), new("Transfers"), new("Uncleared")];
        ReportPeriods = [new("3M"), new("6M", true), new("12M")];
    }

    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial string Greeting { get; set; } = "Good morning";
    [ObservableProperty] public partial string UserInitials { get; set; } = "LM";
    [ObservableProperty] public partial string NetWorth { get; set; } = "£0";
    [ObservableProperty] public partial string SafeToSpend { get; set; } = "£0";
    [ObservableProperty] public partial string CashFlow { get; set; } = "£0";
    [ObservableProperty] public partial string SavingsRate { get; set; } = "0%";
    [ObservableProperty] public partial string MonthlyIncome { get; set; } = "£0";
    [ObservableProperty] public partial string MonthlySpent { get; set; } = "£0";
    [ObservableProperty] public partial string BudgetStatus { get; set; } = "£0 available";
    [ObservableProperty] public partial string ReadyToAssign { get; set; } = "£0";
    [ObservableProperty] public partial string AssignedTotal { get; set; } = "£0";
    [ObservableProperty] public partial string RolloverTotal { get; set; } = "£0";
    [ObservableProperty] public partial double BudgetProgress { get; set; }
    [ObservableProperty] public partial double FinancialHealth { get; set; }
    [ObservableProperty] public partial string FinancialScore { get; set; } = "—";
    [ObservableProperty] public partial DateTime BudgetMonth { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string CurrentMonth { get; set; } = DateTime.Today.ToString("MMMM yyyy");
    [ObservableProperty] public partial string StatusMessage { get; set; } = "";
    [ObservableProperty] public partial bool HasStatusMessage { get; set; }
    [ObservableProperty] public partial bool IsFreshStart { get; set; } = true;
    [ObservableProperty] public partial IReadOnlyList<decimal> CashFlowSeries { get; set; } = Array.Empty<decimal>();

    // Real forecast and reports.
    [ObservableProperty] public partial string ForecastHeadline { get; set; } = "Add income and bills to build a forecast";
    [ObservableProperty] public partial string ForecastBalance { get; set; } = "£0";
    [ObservableProperty] public partial string ForecastIncome { get; set; } = "£0";
    [ObservableProperty] public partial string ForecastOutflow { get; set; } = "£0";
    [ObservableProperty] public partial string ForecastBadge { get; set; } = "NEEDS DATA";
    [ObservableProperty] public partial string ForecastBadgeColor { get; set; } = "#A9C9FF";
    [ObservableProperty] public partial string ReportIncome { get; set; } = "£0";
    [ObservableProperty] public partial string ReportExpenses { get; set; } = "£0";
    [ObservableProperty] public partial string ReportNet { get; set; } = "£0";
    [ObservableProperty] public partial string AverageSpend { get; set; } = "£0/mo";
    [ObservableProperty] public partial IReadOnlyList<decimal> ReportCashFlowSeries { get; set; } = Array.Empty<decimal>();

    // Transaction editor and filtering.
    [ObservableProperty] public partial string SearchText { get; set; } = "";
    [ObservableProperty] public partial Account? SelectedAccount { get; set; }
    [ObservableProperty] public partial Account? SelectedTransferAccount { get; set; }
    [ObservableProperty] public partial BudgetCategory? SelectedCategory { get; set; }
    [ObservableProperty] public partial DateTime TransactionDate { get; set; } = DateTime.Today;
    [ObservableProperty] public partial string Payee { get; set; } = "";
    [ObservableProperty] public partial string AmountText { get; set; } = "";
    [ObservableProperty] public partial string TransactionNote { get; set; } = "";
    [ObservableProperty] public partial bool IsIncome { get; set; }
    [ObservableProperty] public partial bool IsTransfer { get; set; }
    [ObservableProperty] public partial bool IsSplitTransaction { get; set; }
    [ObservableProperty] public partial bool CanSplitTransaction { get; set; } = true;
    [ObservableProperty] public partial bool ShowSingleCategory { get; set; } = true;
    [ObservableProperty] public partial string SplitRemaining { get; set; } = "Enter the transaction amount";
    [ObservableProperty] public partial bool IsCleared { get; set; } = true;
    [ObservableProperty] public partial string TransactionEditorTitle { get; set; } = "New transaction";
    [ObservableProperty] public partial bool IsEditingTransaction { get; set; }
    [ObservableProperty] public partial string FormError { get; set; } = "";

    // Account editor.
    [ObservableProperty] public partial string AccountName { get; set; } = "";
    [ObservableProperty] public partial string OpeningBalanceText { get; set; } = "";
    [ObservableProperty] public partial string SelectedAccountType { get; set; } = nameof(AccountType.Current);
    [ObservableProperty] public partial bool IsEditingAccount { get; set; }
    [ObservableProperty] public partial string ReconcileBalanceText { get; set; } = "";
    [ObservableProperty] public partial string ReconcileCurrentBalance { get; set; } = "£0.00 current balance";
    [ObservableProperty] public partial bool HasArchivedAccounts { get; set; }
    public IReadOnlyList<string> AccountTypes { get; } = Enum.GetNames<AccountType>();

    // Budget editor and real allocation transfer.
    [ObservableProperty] public partial string CategoryName { get; set; } = "";
    [ObservableProperty] public partial string BudgetTargetText { get; set; } = "";
    [ObservableProperty] public partial bool IsEditingCategory { get; set; }
    [ObservableProperty] public partial BudgetCategory? MoveFromCategory { get; set; }
    [ObservableProperty] public partial BudgetCategory? MoveToCategory { get; set; }
    [ObservableProperty] public partial string MoveAmountText { get; set; } = "";
    [ObservableProperty] public partial string MoveAvailableText { get; set; } = "Select a source envelope";
    [ObservableProperty] public partial BudgetCategory? AssignToCategory { get; set; }
    [ObservableProperty] public partial string AssignAmountText { get; set; } = "";
    [ObservableProperty] public partial string AssignAvailableText { get; set; } = "£0 ready";

    // Goal editor and funding.
    [ObservableProperty] public partial string GoalName { get; set; } = "";
    [ObservableProperty] public partial string GoalTargetText { get; set; } = "";
    [ObservableProperty] public partial string GoalSavedText { get; set; } = "";
    [ObservableProperty] public partial DateTime GoalDate { get; set; } = DateTime.Today.AddMonths(12);
    [ObservableProperty] public partial bool IsEditingGoal { get; set; }
    [ObservableProperty] public partial string FundingGoalName { get; set; } = "";
    [ObservableProperty] public partial string FundAmountText { get; set; } = "";
    [ObservableProperty] public partial string FundingProgressText { get; set; } = "";

    // Bill editor.
    [ObservableProperty] public partial string BillName { get; set; } = "";
    [ObservableProperty] public partial string BillAmountText { get; set; } = "";
    [ObservableProperty] public partial DateTime BillDueDate { get; set; } = DateTime.Today.AddDays(7);
    [ObservableProperty] public partial string SelectedBillFrequency { get; set; } = nameof(RecurrenceFrequency.Monthly);
    [ObservableProperty] public partial bool IsSubscription { get; set; }
    [ObservableProperty] public partial bool IsEditingBill { get; set; }
    [ObservableProperty] public partial BudgetCategory? SelectedBillCategory { get; set; }
    public IReadOnlyList<string> BillFrequencies { get; } = Enum.GetNames<RecurrenceFrequency>();

    public ObservableCollection<Account> Accounts { get; } = [];
    public ObservableCollection<BudgetCategory> BudgetCategories { get; } = [];
    public ObservableCollection<AccountItem> AccountCards { get; } = [];
    public ObservableCollection<AccountItem> ArchivedAccountCards { get; } = [];
    public ObservableCollection<TransactionItem> Transactions { get; } = [];
    public ObservableCollection<TransactionSplitEditorItem> TransactionSplits { get; } = [];
    public ObservableCollection<TransactionItem> RecentTransactions { get; } = [];
    public ObservableCollection<CategoryItem> Categories { get; } = [];
    public ObservableCollection<GoalItem> Goals { get; } = [];
    public ObservableCollection<BillItem> Bills { get; } = [];
    public double GoalsListHeight { get; private set; } = 190;
    public double BillsListHeight { get; private set; } = 190;
    public ObservableCollection<ReportCategoryItem> ReportCategories { get; } = [];
    public ObservableCollection<FilterItem> TransactionFilters { get; }
    public ObservableCollection<FilterItem> ReportPeriods { get; }

    [RelayCommand]
    public async Task LoadAsync()
    {
        await LoadCoreAsync(forceRefresh: true);
    }

    public Task LoadIfStaleAsync() => LoadCoreAsync(forceRefresh: false);

    private async Task LoadCoreAsync(bool forceRefresh)
    {
        await _loadGate.WaitAsync();
        try
        {
            if (!forceRefresh && _data is not null && DateTime.UtcNow - _lastLoadedUtc < TimeSpan.FromSeconds(30)) return;
            IsBusy = true;
            if (!_syncAttempted) { _syncAttempted = true; await _sync.TrySyncAsync(); }
            var profileName = await _session.GetDisplayNameAsync();
            UserInitials = MakeInitials(profileName);
            _data = await _database.LoadAsync();
            var accountId = SelectedAccount?.Id; var transferAccountId = SelectedTransferAccount?.Id; var categoryId = SelectedCategory?.Id;
            Accounts.ReplaceWith(_data.Accounts.Where(x => !x.IsArchived)); BudgetCategories.ReplaceWith(_data.Categories.OrderBy(x => x.SortOrder));
            SelectedAccount = Accounts.FirstOrDefault(x => x.Id == accountId) ?? Accounts.FirstOrDefault();
            SelectedTransferAccount = Accounts.FirstOrDefault(x => x.Id == transferAccountId) ?? Accounts.FirstOrDefault(x => x.Id != SelectedAccount?.Id);
            SelectedCategory = BudgetCategories.FirstOrDefault(x => x.Id == categoryId) ?? BudgetCategories.FirstOrDefault();
            BuildProductState();
            await _reminders.RescheduleAsync(_data.Bills);
            _lastLoadedUtc = DateTime.UtcNow;
        }
        finally { IsBusy = false; _loadGate.Release(); }
    }

    public async Task ReloadAfterExternalSyncAsync()
    {
        _syncAttempted = true;
        await LoadCoreAsync(forceRefresh: true);
    }

    private void BuildProductState()
    {
        if (_data is null) return;
        var balances = FinanceEngine.Balances(_data.Accounts, _data.Transactions);
        IsFreshStart = !_data.Transactions.Any() && _data.Accounts.Where(x => !x.IsArchived)
            .All(account => balances.GetValueOrDefault(account.Id) == 0);
        CurrentMonth = BudgetMonth.ToString("MMMM yyyy");
        var upcoming = _data.Bills.Where(x => x.NextDue <= DateTime.Today.AddDays(30)).Sum(x => x.Amount);
        var snapshot = FinanceEngine.BuildSnapshot(_data.Accounts, _data.Transactions, _data.Categories, BudgetMonth, upcoming, _data.Allocations);
        NetWorth = Money(snapshot.NetWorth); SafeToSpend = Money(snapshot.SafeToSpend); CashFlow = SignedMoney(snapshot.Income - snapshot.Expenses);
        SavingsRate = $"{snapshot.SavingsRate:0.#}%"; MonthlyIncome = Money(snapshot.Income); MonthlySpent = Money(snapshot.Expenses);
        var available = snapshot.Categories.Sum(x => x.Available); var remaining = snapshot.Categories.Sum(x => x.Remaining);
        BudgetProgress = available <= 0 ? 0 : Math.Clamp((double)(snapshot.Expenses / available), 0, 1);
        BudgetStatus = $"{Money(remaining)} available"; AssignedTotal = Money(snapshot.Categories.Sum(x => x.Assigned)); RolloverTotal = Money(snapshot.Categories.Sum(x => x.RolledOver));
        ReadyToAssign = SignedMoney(snapshot.Income - snapshot.Categories.Sum(x => x.Assigned));
        var score = CalculateScore(snapshot); FinancialScore = score.ToString(CultureInfo.InvariantCulture); FinancialHealth = score / 100d;
        CashFlowSeries = BuildMonthlySeries(_data.Transactions, 6);
        Categories.ReplaceWith(snapshot.Categories.Select(x => new CategoryItem(x)));
        Goals.ReplaceWith(_data.Goals.OrderBy(x => x.TargetDate).Select(x => new GoalItem(x, FinanceEngine.MonthlyGoalContribution(x, DateTime.Today))));
        Bills.ReplaceWith(_data.Bills.OrderBy(x => x.NextDue).Select(x => new BillItem(x)));
        GoalsListHeight = Goals.Count == 0 ? 190 : Math.Clamp(Goals.Count * 156d, 156, 780);
        BillsListHeight = Bills.Count == 0 ? 190 : Math.Clamp(Bills.Count * 112d, 112, 672);
        OnPropertyChanged(nameof(GoalsListHeight)); OnPropertyChanged(nameof(BillsListHeight));
        AccountCards.ReplaceWith(_data.Accounts.Where(x => !x.IsArchived).Select(x => new AccountItem(x, balances.GetValueOrDefault(x.Id))));
        ArchivedAccountCards.ReplaceWith(_data.Accounts.Where(x => x.IsArchived).Select(x => new AccountItem(x, balances.GetValueOrDefault(x.Id)))); HasArchivedAccounts = ArchivedAccountCards.Count > 0;
        RecentTransactions.ReplaceWith(MapTransactions(_data.Transactions.OrderByDescending(x => x.Date).Take(4)));
        ApplySearch(); BuildForecast(); BuildReports();
        Greeting = DateTime.Now.Hour < 12 ? "Good morning" : DateTime.Now.Hour < 18 ? "Good afternoon" : "Good evening";
    }

    private void BuildForecast()
    {
        if (_data is null) return; var forecast = FinanceEngine.BuildForecast(_data.Accounts, _data.Transactions, _data.Bills, DateTime.Today);
        ForecastBalance = SignedMoney(forecast.ProjectedBalance); ForecastIncome = Money(forecast.ExpectedIncome); ForecastOutflow = Money(forecast.ScheduledOutflow);
        var hasData = _data.Transactions.Any(x => x.Type == TransactionType.Income) || _data.Bills.Count > 0;
        ForecastHeadline = !hasData ? "Add income and bills to build a forecast" : forecast.StaysPositive ? $"Projected to finish at {Money(forecast.ProjectedBalance)}" : $"Cash gap of {Money(Math.Abs(forecast.LowestBalance))} detected";
        ForecastBadge = !hasData ? "NEEDS DATA" : forecast.StaysPositive ? "ON TRACK" : "ACTION NEEDED";
        ForecastBadgeColor = !hasData ? "#A9C9FF" : forecast.StaysPositive ? "#79B5FF" : "#F17889";
    }

    private void BuildReports()
    {
        if (_data is null) return; var months = int.Parse(ReportPeriods.Single(x => x.IsSelected).Name.TrimEnd('M'), CultureInfo.InvariantCulture);
        var start = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-(months - 1)); var items = _data.Transactions.Where(x => x.Date >= start).ToList();
        var income = items.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount); var expenses = items.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
        ReportIncome = Money(income); ReportExpenses = Money(expenses); ReportNet = SignedMoney(income - expenses); AverageSpend = $"{Money(expenses / months)}/mo";
        var monthlyCashFlow = items.GroupBy(x => (x.Date.Year, x.Date.Month)).ToDictionary(group => group.Key,
            group => group.Sum(x => x.Type == TransactionType.Income ? x.Amount : x.Type == TransactionType.Expense ? -x.Amount : 0));
        ReportCashFlowSeries = Enumerable.Range(0, months).Select(i => start.AddMonths(i))
            .Select(month => monthlyCashFlow.GetValueOrDefault((month.Year, month.Month))).ToArray();
        var categoryLookup = _data.Categories.ToDictionary(x => x.Id);
        var categoryTotals = new Dictionary<Guid, decimal>();
        decimal uncategorisedTotal = 0;
        foreach (var item in items.Where(x => x.Type == TransactionType.Expense))
        {
            if (item.Splits is { Count: > 0 })
            {
                foreach (var split in item.Splits) categoryTotals[split.CategoryId] = categoryTotals.GetValueOrDefault(split.CategoryId) + split.Amount;
            }
            else if (item.CategoryId is { } categoryId) categoryTotals[categoryId] = categoryTotals.GetValueOrDefault(categoryId) + item.Amount;
            else uncategorisedTotal += item.Amount;
        }
        var categoryAmounts = categoryTotals.Select(x => new { CategoryId = (Guid?)x.Key, Amount = x.Value }).ToList();
        if (uncategorisedTotal > 0) categoryAmounts.Add(new { CategoryId = (Guid?)null, Amount = uncategorisedTotal });
        var max = Math.Max(1, categoryAmounts.Select(x => x.Amount).DefaultIfEmpty().Max());
        ReportCategories.ReplaceWith(categoryAmounts.Select(group =>
        {
            var category = group.CategoryId is { } id && categoryLookup.TryGetValue(id, out var value) ? value : null; var amount = group.Amount;
            return new ReportCategoryItem(category?.Name ?? "Uncategorised", category?.Color ?? "#82939A", Money(amount), (double)(amount / max));
        }).OrderByDescending(x => x.Progress));
    }

    partial void OnSearchTextChanged(string value) => DebounceSearch();
    partial void OnMoveFromCategoryChanged(BudgetCategory? value) => RefreshMoveAvailable();
    partial void OnAmountTextChanged(string value) => RefreshSplitRemaining();
    partial void OnIsSplitTransactionChanged(bool value)
    {
        if (value && TransactionSplits.Count == 0) { AddSplitRow(); AddSplitRow(); }
        if (!value) TransactionSplits.Clear();
        ShowSingleCategory = !value && !IsTransfer;
        RefreshSplitRemaining();
    }

    private void ApplySearch()
    {
        if (_data is null) return; var selected = TransactionFilters.Single(x => x.IsSelected).Name;
        var query = _data.Transactions.Where(x => string.IsNullOrWhiteSpace(SearchText) || x.Payee.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || x.Note.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        query = selected switch { "Income" => query.Where(x => x.Type == TransactionType.Income), "Expenses" => query.Where(x => x.Type == TransactionType.Expense), "Transfers" => query.Where(x => x.Type == TransactionType.Transfer), "Uncleared" => query.Where(x => !x.IsCleared), _ => query };
        _filteredTransactions = query.OrderByDescending(x => x.Date).ToList();
        Transactions.Clear(); _transactionsShown = 0;
        LoadMoreTransactions();
    }

    private void DebounceSearch()
    {
        _searchDebounce?.Cancel();
        var source = new CancellationTokenSource();
        _searchDebounce = source;
        _ = ApplySearchAfterPauseAsync(source);
    }

    private async Task ApplySearchAfterPauseAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(180, source.Token);
            if (ReferenceEquals(_searchDebounce, source)) { _searchDebounce = null; ApplySearch(); }
        }
        catch (OperationCanceledException) { }
        finally { source.Dispose(); }
    }

    [RelayCommand]
    public void LoadMoreTransactions()
    {
        if (_transactionsShown >= _filteredTransactions.Count) return;
        var page = _filteredTransactions.Skip(_transactionsShown).Take(TransactionPageSize).ToList();
        foreach (var transaction in MapTransactions(page)) Transactions.Add(transaction);
        _transactionsShown += page.Count;
    }

    private IEnumerable<TransactionItem> MapTransactions(IEnumerable<MoneyTransaction> transactions)
    {
        if (_data is null) return []; var categories = _data.Categories.ToDictionary(x => x.Id); var accounts = _data.Accounts.ToDictionary(x => x.Id);
        return transactions.Select(x => new TransactionItem(x, x.CategoryId is { } id && categories.TryGetValue(id, out var c) ? c : null, accounts.GetValueOrDefault(x.AccountId), x.TransferAccountId is { } transferId ? accounts.GetValueOrDefault(transferId) : null));
    }

    [RelayCommand]
    public void SetTransactionFilter(FilterItem item)
    {
        foreach (var filter in TransactionFilters) filter.IsSelected = filter == item; ApplySearch(); Tap();
    }

    [RelayCommand]
    public void SetReportPeriod(FilterItem item)
    {
        foreach (var period in ReportPeriods) period.IsSelected = period == item; BuildReports(); Tap();
    }

    [RelayCommand]
    public async Task AddTransactionAsync()
    {
        _editingTransactionId = null; IsEditingTransaction = false; TransactionEditorTitle = "New transaction"; Payee = AmountText = TransactionNote = FormError = ""; TransactionDate = DateTime.Today; IsIncome = false; IsTransfer = false; IsSplitTransaction = false; TransactionSplits.Clear(); IsCleared = true; SelectedTransferAccount = Accounts.FirstOrDefault(x => x.Id != SelectedAccount?.Id); Tap(); await Shell.Current.GoToAsync("transaction-editor");
    }

    [RelayCommand]
    public async Task EditTransactionAsync(TransactionItem item)
    {
        var source = _data!.Transactions.Single(x => x.Id == item.Id); _editingTransactionId = source.Id; IsEditingTransaction = true; TransactionEditorTitle = "Edit transaction";
        SelectedAccount = Accounts.FirstOrDefault(x => x.Id == source.AccountId); SelectedTransferAccount = Accounts.FirstOrDefault(x => x.Id == source.TransferAccountId); SelectedCategory = BudgetCategories.FirstOrDefault(x => x.Id == source.CategoryId);
        TransactionSplits.Clear(); foreach (var split in source.Splits ?? []) AddSplitRow(split); IsSplitTransaction = TransactionSplits.Count > 0;
        TransactionDate = source.Date; Payee = source.Payee; AmountText = source.Amount.ToString("0.##", CultureInfo.InvariantCulture); TransactionNote = source.Note; IsIncome = source.Type == TransactionType.Income; IsTransfer = source.Type == TransactionType.Transfer; IsCleared = source.IsCleared; FormError = ""; RefreshSplitRemaining(); Tap();
        await Shell.Current.GoToAsync("transaction-editor");
    }

    [RelayCommand]
    public async Task SaveTransactionAsync()
    {
        FormError = "";
        if (SelectedAccount is null || !TryMoney(AmountText, out var amount) || amount <= 0 || !IsTransfer && string.IsNullOrWhiteSpace(Payee)) { FormError = "Choose an account and enter a payee and positive amount."; return; }
        if (IsTransfer && (SelectedTransferAccount is null || SelectedTransferAccount.Id == SelectedAccount.Id)) { FormError = "Choose a different destination account for this transfer."; return; }
        IReadOnlyList<TransactionSplit>? splits = null;
        if (IsSplitTransaction)
        {
            if (IsIncome || IsTransfer || TransactionSplits.Count < 2 || TransactionSplits.Any(x => x.Category is null || !TryMoney(x.AmountText, out var splitAmount) || splitAmount <= 0)) { FormError = "Every split needs a category and positive amount."; return; }
            splits = TransactionSplits.Select(x => new TransactionSplit(x.Id, x.Category!.Id, ParseMoney(x.AmountText), x.Note.Trim())).ToList();
            if (Math.Abs(splits.Sum(x => x.Amount) - amount) >= 0.005m) { FormError = "Split amounts must equal the transaction total."; return; }
        }
        var type = IsTransfer ? TransactionType.Transfer : IsIncome ? TransactionType.Income : TransactionType.Expense;
        var transferPayee = IsTransfer ? $"Transfer to {SelectedTransferAccount!.Name}" : Payee.Trim();
        var item = new MoneyTransaction(_editingTransactionId ?? Guid.NewGuid(), SelectedAccount.Id, IsTransfer || IsSplitTransaction ? null : SelectedCategory?.Id, TransactionDate, amount, type, transferPayee, TransactionNote.Trim(), IsCleared, IsTransfer ? SelectedTransferAccount!.Id : null, splits);
        await _database.SaveTransactionAsync(item); await SyncReloadAsync("Transaction saved"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task DeleteTransactionAsync(TransactionItem item)
    {
        if (!await ConfirmAsync("Delete transaction?", $"Remove {item.Payee} {item.AmountText}?")) return;
        await _database.DeleteTransactionAsync(item.Id); await SyncReloadAsync("Transaction deleted");
    }

    [RelayCommand] public void AddTransactionSplit() { if (!IsSplitTransaction) { IsSplitTransaction = true; return; } AddSplitRow(); RefreshSplitRemaining(); }
    [RelayCommand] public void RemoveTransactionSplit(TransactionSplitEditorItem item) { TransactionSplits.Remove(item); if (TransactionSplits.Count == 0) IsSplitTransaction = false; RefreshSplitRemaining(); }

    [RelayCommand]
    public async Task AddAccountAsync()
    {
        _editingAccountId = null; IsEditingAccount = false; AccountName = OpeningBalanceText = ReconcileBalanceText = FormError = ""; ReconcileCurrentBalance = "£0.00 current balance"; SelectedAccountType = nameof(AccountType.Current); Tap(); await Shell.Current.GoToAsync("account-editor");
    }

    [RelayCommand]
    public async Task EditAccountAsync(AccountItem item)
    {
        var source = _data!.Accounts.Single(x => x.Id == item.Id); var current = FinanceEngine.Balance(source, _data.Transactions); _editingAccountId = source.Id; IsEditingAccount = true; AccountName = source.Name; OpeningBalanceText = source.OpeningBalance.ToString("0.##", CultureInfo.InvariantCulture); ReconcileBalanceText = current.ToString("0.00", CultureInfo.InvariantCulture); ReconcileCurrentBalance = $"{Money(current)} current balance"; SelectedAccountType = source.Type.ToString(); FormError = ""; Tap(); await Shell.Current.GoToAsync("account-editor");
    }

    [RelayCommand]
    public async Task SaveAccountAsync()
    {
        FormError = ""; if (string.IsNullOrWhiteSpace(AccountName) || !TryMoney(string.IsNullOrWhiteSpace(OpeningBalanceText) ? "0" : OpeningBalanceText, out var balance) || !Enum.TryParse<AccountType>(SelectedAccountType, out var type)) { FormError = "Enter a valid account name and opening balance."; return; }
        var source = _editingAccountId is { } id ? _data!.Accounts.Single(x => x.Id == id) : null; var color = AccountColor(type);
        await _database.SaveAccountAsync(new(source?.Id ?? Guid.NewGuid(), AccountName.Trim(), type, "GBP", balance, color, source?.IsArchived ?? false)); await SyncReloadAsync("Account saved"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task ArchiveAccountAsync()
    {
        if (_editingAccountId is not { } id || !await ConfirmAsync("Archive account?", "Its history remains in reports and you can restore it later from the database.")) return;
        var source = _data!.Accounts.Single(x => x.Id == id); await _database.SaveAccountAsync(source with { IsArchived = true }); await SyncReloadAsync("Account archived"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task ReconcileAccountAsync()
    {
        FormError = "";
        if (_editingAccountId is not { } accountId || _data is null || !TryMoney(ReconcileBalanceText, out var statementBalance)) { FormError = "Enter the closing balance from your bank statement."; return; }
        var account = _data.Accounts.Single(x => x.Id == accountId); var current = FinanceEngine.Balance(account, _data.Transactions); var difference = statementBalance - current;
        var message = Math.Abs(difference) < 0.005m ? "This matches. All activity in this account will be marked cleared." : $"Lumina will create a {Money(Math.Abs(difference))} reconciliation adjustment and mark the account activity cleared.";
        if (!await ConfirmAsync("Reconcile account?", message)) return;
        var changed = _data.Transactions.Where(x => !x.IsCleared && (x.AccountId == accountId || x.TransferAccountId == accountId)).Select(x => x with { IsCleared = true }).ToList();
        if (changed.Count > 0) await _database.SaveTransactionsAsync(changed);
        if (Math.Abs(difference) >= 0.005m)
        {
            var adjustment = new MoneyTransaction(Guid.NewGuid(), accountId, null, DateTime.Today, Math.Abs(difference), difference > 0 ? TransactionType.Income : TransactionType.Expense, "Reconciliation adjustment", $"Matched to statement balance {statementBalance:0.00}", true);
            await _database.SaveTransactionAsync(adjustment);
        }
        await SyncReloadAsync("Account reconciled"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task RestoreAccountAsync(AccountItem item)
    {
        var source = _data!.Accounts.Single(x => x.Id == item.Id); await _database.SaveAccountAsync(source with { IsArchived = false }); await SyncReloadAsync("Account restored");
    }

    [RelayCommand]
    public async Task AddCategoryAsync()
    {
        _editingCategoryId = null; IsEditingCategory = false; CategoryName = BudgetTargetText = FormError = ""; Tap(); await Shell.Current.GoToAsync("budget-editor");
    }

    [RelayCommand]
    public async Task EditCategoryAsync(CategoryItem item)
    {
        _editingCategoryId = item.Id; IsEditingCategory = true; CategoryName = item.Name; BudgetTargetText = item.TargetValue.ToString("0.##", CultureInfo.InvariantCulture); FormError = ""; Tap(); await Shell.Current.GoToAsync("budget-editor");
    }

    [RelayCommand]
    public async Task SaveCategoryAsync()
    {
        FormError = ""; if (string.IsNullOrWhiteSpace(CategoryName) || !TryMoney(BudgetTargetText, out var target) || target < 0) { FormError = "Enter a category name and valid monthly target."; return; }
        var data = _data ?? await _database.LoadAsync(); var source = _editingCategoryId is { } id ? data.Categories.Single(x => x.Id == id) : null; var palette = new[] { "#3E79DB", "#5797F4", "#7AB5FF", "#7890EE", "#62A9D6" };
        await _database.SaveCategoryAsync(new(source?.Id ?? Guid.NewGuid(), CategoryName.Trim(), source?.Icon ?? "◆", source?.Color ?? palette[data.Categories.Count % palette.Length], target, source?.SortOrder ?? data.Categories.Count + 1)); await SyncReloadAsync("Budget category saved"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task DeleteCategoryAsync()
    {
        if (_editingCategoryId is not { } id) return; if (_data!.Transactions.Any(x => x.CategoryId == id || x.Splits?.Any(s => s.CategoryId == id) == true)) { FormError = "This category has transactions or splits. Recategorise them before deleting it."; return; }
        if (!await ConfirmAsync("Delete category?", "This removes its targets and monthly assignments.")) return; await _database.DeleteCategoryAsync(id); await SyncReloadAsync("Category deleted"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task OpenBudgetMoveAsync()
    {
        if (BudgetCategories.Count < 2) { ShowStatus("Create at least two categories to move money."); return; }
        MoveFromCategory = BudgetCategories[0]; MoveToCategory = BudgetCategories[1]; MoveAmountText = FormError = ""; RefreshMoveAvailable(); Tap(); await Shell.Current.GoToAsync("budget-move");
    }

    [RelayCommand]
    public async Task SaveBudgetMoveAsync()
    {
        FormError = ""; if (MoveFromCategory is null || MoveToCategory is null || MoveFromCategory.Id == MoveToCategory.Id || !TryMoney(MoveAmountText, out var amount) || amount <= 0) { FormError = "Choose two different envelopes and enter a positive amount."; return; }
        var snapshot = FinanceEngine.BuildSnapshot(_data!.Accounts, _data.Transactions, _data.Categories, BudgetMonth, 0, _data.Allocations); var source = snapshot.Categories.Single(x => x.CategoryId == MoveFromCategory.Id); var destination = snapshot.Categories.Single(x => x.CategoryId == MoveToCategory.Id);
        if (amount > Math.Max(0, source.Remaining)) { FormError = $"Only {Money(Math.Max(0, source.Remaining))} is available in {source.Name}."; return; }
        var assignedReduction = Math.Min(source.Assigned, amount); var rolloverReduction = amount - assignedReduction;
        var sourceAllocation = CurrentAllocation(source.CategoryId, source.Assigned - assignedReduction, Math.Max(0, source.RolledOver - rolloverReduction));
        var destinationAllocation = CurrentAllocation(destination.CategoryId, destination.Assigned + amount, destination.RolledOver);
        await _database.SaveAllocationAsync(sourceAllocation); await _database.SaveAllocationAsync(destinationAllocation); await SyncReloadAsync($"Moved {Money(amount)} to {destination.Name}"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task OpenBudgetAssignAsync()
    {
        if (BudgetCategories.Count == 0) { ShowStatus("Create a category before assigning money."); return; }
        AssignToCategory = BudgetCategories[0]; AssignAmountText = FormError = ""; var ready = CurrentReadyToAssign(); AssignAvailableText = $"{SignedMoney(ready)} ready to assign"; Tap(); await Shell.Current.GoToAsync("budget-assign");
    }

    [RelayCommand]
    public async Task SaveBudgetAssignAsync()
    {
        FormError = ""; if (AssignToCategory is null || !TryMoney(AssignAmountText, out var amount) || amount <= 0) { FormError = "Choose an envelope and enter a positive amount."; return; }
        var ready = CurrentReadyToAssign(); if (amount > Math.Max(0, ready)) { FormError = $"Only {Money(Math.Max(0, ready))} is ready to assign."; return; }
        var snapshot = FinanceEngine.BuildSnapshot(_data!.Accounts, _data.Transactions, _data.Categories, BudgetMonth, 0, _data.Allocations); var destination = snapshot.Categories.Single(x => x.CategoryId == AssignToCategory.Id);
        await _database.SaveAllocationAsync(CurrentAllocation(destination.CategoryId, destination.Assigned + amount, destination.RolledOver)); await SyncReloadAsync($"Assigned {Money(amount)} to {destination.Name}"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task ChangeBudgetMonthAsync(string direction)
    {
        BudgetMonth = direction == "previous" ? BudgetMonth.AddMonths(-1) : BudgetMonth.AddMonths(1); BuildProductState(); Tap(); await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task AddGoalAsync()
    {
        _editingGoalId = null; IsEditingGoal = false; GoalName = GoalTargetText = GoalSavedText = FormError = ""; GoalDate = DateTime.Today.AddMonths(12); Tap(); await Shell.Current.GoToAsync("goal-editor");
    }

    [RelayCommand]
    public async Task EditGoalAsync(GoalItem item)
    {
        var source = _data!.Goals.Single(x => x.Id == item.Id); _editingGoalId = source.Id; IsEditingGoal = true; GoalName = source.Name; GoalTargetText = source.TargetAmount.ToString("0.##", CultureInfo.InvariantCulture); GoalSavedText = source.SavedAmount.ToString("0.##", CultureInfo.InvariantCulture); GoalDate = source.TargetDate; FormError = ""; Tap(); await Shell.Current.GoToAsync("goal-editor");
    }

    [RelayCommand]
    public async Task SaveGoalAsync()
    {
        FormError = ""; if (string.IsNullOrWhiteSpace(GoalName) || !TryMoney(GoalTargetText, out var target) || target <= 0 || !TryMoney(string.IsNullOrWhiteSpace(GoalSavedText) ? "0" : GoalSavedText, out var saved) || saved < 0 || saved > target) { FormError = "Enter a goal name; saved money must be between zero and the target."; return; }
        var source = _editingGoalId is { } id ? _data!.Goals.Single(x => x.Id == id) : null; await _database.SaveGoalAsync(new(source?.Id ?? Guid.NewGuid(), GoalName.Trim(), target, saved, GoalDate, source?.Icon ?? "◆", source?.Color ?? "#5797F4")); await SyncReloadAsync("Goal saved"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task OpenFundGoalAsync(GoalItem item)
    {
        var source = _data!.Goals.Single(x => x.Id == item.Id); _fundingGoalId = source.Id; FundingGoalName = source.Name; FundAmountText = FormError = ""; FundingProgressText = $"{Money(source.SavedAmount)} saved of {Money(source.TargetAmount)}"; Tap(); await Shell.Current.GoToAsync("goal-fund");
    }

    [RelayCommand]
    public async Task SaveGoalFundingAsync()
    {
        FormError = ""; if (_fundingGoalId is not { } id || !TryMoney(FundAmountText, out var amount) || amount <= 0) { FormError = "Enter a positive contribution."; return; }
        var source = _data!.Goals.Single(x => x.Id == id); await _database.SaveGoalAsync(source with { SavedAmount = Math.Min(source.TargetAmount, source.SavedAmount + amount) }); await SyncReloadAsync($"Added {Money(amount)} to {source.Name}"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task DeleteGoalAsync()
    {
        if (_editingGoalId is not { } id || !await ConfirmAsync("Delete goal?", "Its progress will be permanently removed.")) return; await _database.DeleteGoalAsync(id); await SyncReloadAsync("Goal deleted"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task AddBillAsync()
    {
        _editingBillId = null; IsEditingBill = false; BillName = BillAmountText = FormError = ""; BillDueDate = DateTime.Today.AddDays(7); SelectedBillFrequency = nameof(RecurrenceFrequency.Monthly); SelectedBillCategory = null; IsSubscription = false; Tap(); await Shell.Current.GoToAsync("bill-editor");
    }

    [RelayCommand]
    public async Task EditBillAsync(BillItem item)
    {
        var source = _data!.Bills.Single(x => x.Id == item.Id); _editingBillId = source.Id; IsEditingBill = true; BillName = source.Name; BillAmountText = source.Amount.ToString("0.##", CultureInfo.InvariantCulture); BillDueDate = source.NextDue; SelectedBillFrequency = source.Frequency.ToString(); SelectedBillCategory = BudgetCategories.FirstOrDefault(x => x.Id == source.CategoryId); IsSubscription = source.IsSubscription; FormError = ""; Tap(); await Shell.Current.GoToAsync("bill-editor");
    }

    [RelayCommand]
    public async Task SaveBillAsync()
    {
        FormError = ""; if (string.IsNullOrWhiteSpace(BillName) || !TryMoney(BillAmountText, out var amount) || amount <= 0 || !Enum.TryParse<RecurrenceFrequency>(SelectedBillFrequency, out var frequency)) { FormError = "Enter a bill name and valid positive amount."; return; }
        var source = _editingBillId is { } id ? _data!.Bills.Single(x => x.Id == id) : null;
        await _database.SaveBillAsync(new(source?.Id ?? Guid.NewGuid(), BillName.Trim(), amount, BillDueDate, frequency, SelectedBillCategory?.Id, IsSubscription));
        await SyncReloadAsync("Commitment saved");
        if (source is null && _reminders.ShouldOfferAfterBillCreated)
        {
            _reminders.MarkEnableOfferShown();
            var enable = await Shell.Current.DisplayAlertAsync(
                "Turn on bill reminders?",
                "Lumina Money can alert you before this commitment is due. You stay in control and can change timing at any time.",
                "Allow reminders", "Not now");
            if (enable)
            {
                var data = await _database.LoadAsync();
                var result = await _reminders.SetEnabledAsync(true, data.Bills);
                ShowStatus(result.Message);
            }
        }
        await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task MarkBillPaidAsync(BillItem item)
    {
        var account = Accounts.FirstOrDefault(); if (account is null) { ShowStatus("Add an account before recording a bill payment."); return; }
        var source = _data!.Bills.Single(x => x.Id == item.Id); var transaction = new MoneyTransaction(Guid.NewGuid(), account.Id, source.CategoryId, DateTime.Today, source.Amount, TransactionType.Expense, source.Name, "Recurring payment", true);
        await _database.SaveTransactionAsync(transaction); await _database.SaveBillAsync(source with { NextDue = FinanceEngine.NextOccurrence(source.NextDue, source.Frequency) }); await SyncReloadAsync($"{source.Name} marked paid");
    }

    [RelayCommand]
    public async Task SkipBillAsync(BillItem item)
    {
        var source = _data!.Bills.Single(x => x.Id == item.Id); await _database.SaveBillAsync(source with { NextDue = FinanceEngine.NextOccurrence(source.NextDue, source.Frequency) }); await SyncReloadAsync($"Skipped this {source.Name} occurrence");
    }

    [RelayCommand]
    public async Task DeleteBillAsync()
    {
        if (_editingBillId is not { } id || !await ConfirmAsync("Delete commitment?", "Future forecasts will no longer include it.")) return; await _database.DeleteBillAsync(id); await SyncReloadAsync("Commitment deleted"); await Shell.Current.GoToAsync("..");
    }

    [RelayCommand]
    public async Task ImportCsvAsync()
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Import bank CSV" }); if (file is null) return;
            var account = SelectedAccount ?? Accounts.FirstOrDefault(); if (account is null) { ShowStatus("Add an account before importing transactions."); return; }
            await using var stream = await file.OpenReadAsync(); using var reader = new StreamReader(stream); var text = await reader.ReadToEndAsync();
            var imported = ParseCsv(text, account.Id).ToList(); if (imported.Count == 0) { ShowStatus("No valid transactions found. Use Date,Payee,Amount,Type,Category,Note columns."); return; }
            await _database.SaveTransactionsAsync(imported); await SyncReloadAsync($"Imported {imported.Count} transactions");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { ShowStatus($"Import failed: {ex.Message}"); }
    }

    [RelayCommand]
    public async Task ExportCsvAsync()
    {
        if (_data is null || _data.Transactions.Count == 0) { ShowStatus("There are no transactions to export yet."); return; }
        var categories = _data.Categories.ToDictionary(x => x.Id, x => x.Name); var rows = new List<string> { "Date,Payee,Amount,Type,Category,Note,Cleared" };
        rows.AddRange(_data.Transactions.OrderByDescending(x => x.Date).Select(x => string.Join(',', Csv(x.Date.ToString("yyyy-MM-dd")), Csv(x.Payee), x.Amount.ToString("0.00", CultureInfo.InvariantCulture), x.Type, Csv(x.CategoryId is { } id && categories.TryGetValue(id, out var name) ? name : ""), Csv(x.Note), x.IsCleared)));
        var path = Path.Combine(FileSystem.CacheDirectory, $"lumina-transactions-{DateTime.Today:yyyyMMdd}.csv"); await File.WriteAllLinesAsync(path, rows);
        await Share.Default.RequestAsync(new ShareFileRequest("Export Lumina Money transactions", new ShareFile(path))); ShowStatus("CSV export prepared");
    }

    [RelayCommand] public Task OpenTransactionsAsync() => NavigateAsync("//transactions");
    [RelayCommand] public Task OpenBudgetAsync() => NavigateAsync("//budget");
    [RelayCommand] public Task OpenAccountsAsync() => NavigateAsync("//accounts");
    [RelayCommand] public Task OpenReportsAsync() => NavigateAsync("reports");
    [RelayCommand] public Task OpenSettingsAsync() => NavigateAsync("settings");
    [RelayCommand] public async Task OpenIncomeTransactionsAsync() { SelectFilter("Income"); await NavigateAsync("//transactions"); }
    [RelayCommand] public async Task OpenExpenseTransactionsAsync() { SelectFilter("Expenses"); await NavigateAsync("//transactions"); }

    private async Task NavigateAsync(string route) { Tap(); await Shell.Current.GoToAsync(route); }
    public void ResetProfile() { _syncAttempted = false; _data = null; UserInitials = "LM"; }
    private async Task SyncReloadAsync(string message)
    {
        var synced = await _sync.TrySyncAsync();
        await LoadAsync();
        ShowStatus(synced ? message : $"{message} locally · sync pending");
        Tap(HapticFeedbackType.LongPress);
    }
    private BudgetAllocation CurrentAllocation(Guid categoryId, decimal assigned, decimal rollover)
    {
        var existing = _data!.Allocations.SingleOrDefault(x => x.CategoryId == categoryId && x.Year == BudgetMonth.Year && x.Month == BudgetMonth.Month);
        return new(existing?.Id ?? Guid.NewGuid(), categoryId, BudgetMonth.Year, BudgetMonth.Month, assigned, rollover);
    }
    private void RefreshMoveAvailable()
    {
        if (_data is null || MoveFromCategory is null) { MoveAvailableText = "Select a source envelope"; return; }
        var snapshot = FinanceEngine.BuildSnapshot(_data.Accounts, _data.Transactions, _data.Categories, BudgetMonth, 0, _data.Allocations); var source = snapshot.Categories.Single(x => x.CategoryId == MoveFromCategory.Id); MoveAvailableText = $"{Money(Math.Max(0, source.Remaining))} available to move";
    }
    private decimal CurrentReadyToAssign()
    {
        if (_data is null) return 0; var snapshot = FinanceEngine.BuildSnapshot(_data.Accounts, _data.Transactions, _data.Categories, BudgetMonth, 0, _data.Allocations); return snapshot.Income - snapshot.Categories.Sum(x => x.Assigned);
    }
    private void SelectFilter(string name) { foreach (var item in TransactionFilters) item.IsSelected = item.Name == name; ApplySearch(); }
    private IEnumerable<MoneyTransaction> ParseCsv(string csv, Guid accountId)
    {
        if (_data is null) yield break; var lines = csv.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries); if (lines.Length < 2) yield break;
        var headers = ParseCsvLine(lines[0]).Select((value, index) => (value, index)).ToDictionary(x => x.value.Trim(), x => x.index, StringComparer.OrdinalIgnoreCase);
        if (!headers.TryGetValue("Date", out var dateIndex) || !headers.TryGetValue("Payee", out var payeeIndex) || !headers.TryGetValue("Amount", out var amountIndex)) yield break;
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = ParseCsvLine(lines[i]); if (fields.Count <= Math.Max(dateIndex, Math.Max(payeeIndex, amountIndex)) || !DateTime.TryParse(fields[dateIndex], CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) || !TryMoney(fields[amountIndex], out var rawAmount) || rawAmount == 0 || string.IsNullOrWhiteSpace(fields[payeeIndex])) continue;
            var typeText = headers.TryGetValue("Type", out var typeIndex) && typeIndex < fields.Count ? fields[typeIndex] : ""; var type = typeText.Equals("Income", StringComparison.OrdinalIgnoreCase) || rawAmount > 0 && !typeText.Equals("Expense", StringComparison.OrdinalIgnoreCase) ? TransactionType.Income : TransactionType.Expense;
            var categoryText = headers.TryGetValue("Category", out var categoryIndex) && categoryIndex < fields.Count ? fields[categoryIndex] : ""; var category = _data.Categories.FirstOrDefault(x => x.Name.Equals(categoryText, StringComparison.OrdinalIgnoreCase));
            var note = headers.TryGetValue("Note", out var noteIndex) && noteIndex < fields.Count ? fields[noteIndex] : "Imported from CSV";
            yield return new MoneyTransaction(Guid.NewGuid(), accountId, category?.Id, date, Math.Abs(rawAmount), type, fields[payeeIndex].Trim(), note, true);
        }
    }
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>(); var current = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++) { var ch = line[i]; if (ch == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else if (ch == '"') quoted = !quoted; else if (ch == ',' && !quoted) { result.Add(current.ToString()); current.Clear(); } else current.Append(ch); }
        result.Add(current.ToString()); return result;
    }
    private static string Csv(object? value) { var text = value?.ToString() ?? ""; return $"\"{text.Replace("\"", "\"\"")}\""; }
    private static IReadOnlyList<decimal> BuildMonthlySeries(IEnumerable<MoneyTransaction> transactions, int months)
    {
        var start = DateTime.Today.AddMonths(-months + 1);
        var totals = transactions.GroupBy(x => (x.Date.Year, x.Date.Month)).ToDictionary(group => group.Key,
            group => group.Sum(x => x.Type == TransactionType.Income ? x.Amount : x.Type == TransactionType.Expense ? -x.Amount : 0));
        return Enumerable.Range(0, months).Select(offset => start.AddMonths(offset))
            .Select(month => totals.GetValueOrDefault((month.Year, month.Month))).ToArray();
    }
    private static bool TryMoney(string value, out decimal result) => decimal.TryParse(value, NumberStyles.Currency, CultureInfo.CurrentCulture, out result) || decimal.TryParse(value, NumberStyles.Currency, CultureInfo.InvariantCulture, out result);
    private static decimal ParseMoney(string value) { _ = TryMoney(value, out var result); return result; }
    private void AddSplitRow(TransactionSplit? source = null)
    {
        var item = new TransactionSplitEditorItem(source?.Id ?? Guid.NewGuid(), source is null ? BudgetCategories.FirstOrDefault() : BudgetCategories.FirstOrDefault(x => x.Id == source.CategoryId), source?.Amount.ToString("0.##", CultureInfo.InvariantCulture) ?? "", source?.Note ?? "");
        item.PropertyChanged += (_, _) => RefreshSplitRemaining(); TransactionSplits.Add(item);
    }
    private void RefreshSplitRemaining()
    {
        if (!IsSplitTransaction) { SplitRemaining = "Optional: divide an expense across categories"; return; }
        if (!TryMoney(AmountText, out var total)) { SplitRemaining = "Enter the transaction amount"; return; }
        var assigned = TransactionSplits.Sum(x => TryMoney(x.AmountText, out var amount) ? amount : 0); var remaining = total - assigned;
        SplitRemaining = Math.Abs(remaining) < 0.005m ? "✓ Split total matches" : remaining > 0 ? $"{Money(remaining)} left to assign" : $"{Money(Math.Abs(remaining))} over the total";
    }
    private static int CalculateScore(DashboardSnapshot x) { var savings = Math.Clamp((double)x.SavingsRate, 0, 40); var budget = x.Budgeted <= 0 ? 20 : Math.Max(0, 30 - (double)(x.Expenses / x.Budgeted) * 20); var cash = x.SafeToSpend > 0 ? 30 : 5; return (int)Math.Clamp(Math.Round(savings + budget + cash), 0, 100); }
    private static string AccountColor(AccountType type) => type is AccountType.Savings or AccountType.Investment ? "#62A9D6" : type is AccountType.CreditCard or AccountType.Loan ? "#F17889" : "#5797F4";
    private static string MakeInitials(string? value) { var initials = string.Concat((value ?? "Lumina Money").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0]))); return string.IsNullOrEmpty(initials) ? "LM" : initials; }
    private static string Money(decimal value) => value < 0 ? $"−£{Math.Abs(value):N0}" : $"£{value:N0}";
    private static string SignedMoney(decimal value) => $"{(value >= 0 ? "+" : "−")}£{Math.Abs(value):N0}";
    private static async Task<bool> ConfirmAsync(string title, string message) => await Shell.Current.DisplayAlertAsync(title, message, "Confirm", "Cancel");
    private void ShowStatus(string message)
    {
        _statusDismissal?.Cancel(); _statusDismissal?.Dispose();
        _statusDismissal = new CancellationTokenSource();
        StatusMessage = message; HasStatusMessage = true;
        _ = DismissStatusAsync(_statusDismissal);
    }
    private async Task DismissStatusAsync(CancellationTokenSource source)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(3.5), source.Token); if (_statusDismissal == source) HasStatusMessage = false; }
        catch (OperationCanceledException) { }
    }
    private static void Tap(HapticFeedbackType type = HapticFeedbackType.Click)
    {
        try { HapticFeedback.Default.Perform(type); }
        catch (FeatureNotSupportedException) { }
        catch (PermissionException) { }
    }
}

public partial class FilterItem(string name, bool selected = false) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] public partial bool IsSelected { get; set; } = selected;
    public string Background => IsSelected ? "#17365C" : "#101927";
    public string TextColor => IsSelected ? "#A9C9FF" : "#A7B6CA";
    partial void OnIsSelectedChanged(bool value) { OnPropertyChanged(nameof(Background)); OnPropertyChanged(nameof(TextColor)); }
}

public sealed record TransactionItem(Guid Id, string Payee, string Category, string Account, string DateText, string AmountText, string Color, string StateText, string StateColor, TransactionType Type, bool IsCleared)
{
    public TransactionItem(MoneyTransaction x, BudgetCategory? category, Account? account, Account? destination) : this(x.Id, x.Payee, x.Type == TransactionType.Transfer ? $"{account?.Name ?? "Unknown"} → {destination?.Name ?? "Unknown"}" : x.Splits is { Count: > 0 } ? $"Split · {x.Splits.Count} categories" : category?.Name ?? (x.Type == TransactionType.Income ? "Income" : "Uncategorised"), account?.Name ?? "Unknown account", x.Date.ToString("dd MMM yyyy"), $"{(x.Type == TransactionType.Expense ? "−" : x.Type == TransactionType.Income ? "+" : "⇄ ")}£{x.Amount:N2}", x.Type == TransactionType.Expense ? "#F17889" : x.Type == TransactionType.Transfer ? "#5797F4" : "#79B5FF", x.IsCleared ? "CLEARED" : "PENDING", x.IsCleared ? "#79B5FF" : "#A9C9FF", x.Type, x.IsCleared) { }
}
public partial class TransactionSplitEditorItem(Guid id, BudgetCategory? category, string amountText, string note) : ObservableObject
{
    public Guid Id { get; } = id;
    [ObservableProperty] public partial BudgetCategory? Category { get; set; } = category;
    [ObservableProperty] public partial string AmountText { get; set; } = amountText;
    [ObservableProperty] public partial string Note { get; set; } = note;
}
public sealed record CategoryItem(Guid Id, string Name, string Icon, string Color, string Spent, string Target, string Assigned, string Rollover, decimal TargetValue, double Progress, string Remaining, string StateColor)
{
    public CategoryItem(CategoryProgress x) : this(x.CategoryId, x.Name, x.Icon, x.Color, $"£{x.Spent:N0}", $"Target £{x.Target:N0}", $"£{x.Assigned:N0} assigned", x.RolledOver > 0 ? $"+£{x.RolledOver:N0} rollover" : "No rollover", x.Target, x.Progress, x.IsOverBudget ? $"£{Math.Abs(x.Remaining):N0} over" : $"£{x.Remaining:N0} available", x.IsOverBudget ? "#F17889" : "#79B5FF") { }
}
public sealed record GoalItem(Guid Id, string Name, string Icon, string Color, string ProgressText, string TargetText, double Progress, string MonthlyText, string StatusText, string StatusColor)
{
    public GoalItem(SavingsGoal x, decimal monthly) : this(x.Id, x.Name, x.Icon, x.Color, $"£{x.SavedAmount:N0}", $"of £{x.TargetAmount:N0}", x.TargetAmount == 0 ? 0 : Math.Clamp((double)(x.SavedAmount / x.TargetAmount), 0, 1), $"£{monthly:N0}/month", x.SavedAmount >= x.TargetAmount ? "COMPLETE" : x.TargetDate < DateTime.Today ? "OVERDUE" : "IN PROGRESS", x.SavedAmount >= x.TargetAmount ? "#79B5FF" : x.TargetDate < DateTime.Today ? "#F17889" : "#5797F4") { }
}
public sealed record BillItem(Guid Id, string Name, string Badge, string DueText, string AmountText, string Color, string DueState, string DueStateColor)
{
    public BillItem(RecurringBill x) : this(x.Id, x.Name, x.IsSubscription ? "SUBSCRIPTION" : "BILL", x.NextDue.ToString("ddd, dd MMM"), $"£{x.Amount:N2}", x.IsSubscription ? "#5797F4" : "#62A9D6", x.NextDue.Date < DateTime.Today ? "OVERDUE" : x.NextDue.Date == DateTime.Today ? "DUE TODAY" : $"IN {Math.Max(1, (x.NextDue.Date - DateTime.Today).Days)} DAYS", x.NextDue.Date <= DateTime.Today ? "#F17889" : "#A7B6CA") { }
}
public sealed record AccountItem(Guid Id, string Name, string Type, string Balance, string Color, string Initials, string BalanceLabel)
{
    public AccountItem(Account x, decimal balance) : this(x.Id, x.Name, SplitWords(x.Type.ToString()), balance < 0 ? $"−£{Math.Abs(balance):N2}" : $"£{balance:N2}", x.Color, string.Concat(x.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0]))), x.Type is AccountType.CreditCard or AccountType.Loan ? "BALANCE DUE" : "AVAILABLE") { }
    private static string SplitWords(string value) => string.Concat(value.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
}
public sealed record ReportCategoryItem(string Name, string Color, string Amount, double Progress);
internal static class CollectionExtensions { public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> values) { collection.Clear(); foreach (var value in values) collection.Add(value); } }
