namespace LuminaMoney.Core;

public enum AccountType { Current, Savings, CreditCard, Cash, Investment, Loan }
public enum TransactionType { Expense, Income, Transfer }
public enum RecurrenceFrequency { Weekly, Monthly, Quarterly, Yearly }

public sealed record Account(Guid Id, string Name, AccountType Type, string Currency, decimal OpeningBalance, string Color, bool IsArchived = false);
public sealed record TransactionSplit(Guid Id, Guid CategoryId, decimal Amount, string Note = "");
public sealed record MoneyTransaction(Guid Id, Guid AccountId, Guid? CategoryId, DateTime Date, decimal Amount, TransactionType Type, string Payee, string Note = "", bool IsCleared = true, Guid? TransferAccountId = null, IReadOnlyList<TransactionSplit>? Splits = null);
public sealed record BudgetCategory(Guid Id, string Name, string Icon, string Color, decimal MonthlyTarget, int SortOrder = 0);
public sealed record BudgetAllocation(Guid Id, Guid CategoryId, int Year, int Month, decimal Assigned, decimal RolledOver = 0);
public sealed record SavingsGoal(Guid Id, string Name, decimal TargetAmount, decimal SavedAmount, DateTime TargetDate, string Icon, string Color);
public sealed record RecurringBill(Guid Id, string Name, decimal Amount, DateTime NextDue, RecurrenceFrequency Frequency, Guid? CategoryId, bool IsSubscription = false);

public sealed record CategoryProgress(Guid CategoryId, string Name, string Icon, string Color, decimal Target, decimal Assigned, decimal RolledOver, decimal Spent)
{
    public decimal Available => Assigned + RolledOver;
    public decimal Remaining => Available - Spent;
    public double Progress => Available <= 0 ? 0 : Math.Clamp((double)(Spent / Available), 0, 1);
    public bool IsOverBudget => Spent > Available;
}

public sealed record DashboardSnapshot(decimal NetWorth, decimal Income, decimal Expenses, decimal Budgeted, decimal SafeToSpend, decimal SavingsRate, IReadOnlyList<CategoryProgress> Categories);
public sealed record ForecastSnapshot(decimal StartingBalance, decimal ExpectedIncome, decimal ScheduledOutflow, decimal ProjectedBalance, decimal LowestBalance, IReadOnlyList<decimal> MonthlyBalances)
{
    public bool StaysPositive => LowestBalance >= 0;
}

public static class FinanceEngine
{
    public static decimal Balance(Account account, IEnumerable<MoneyTransaction> transactions)
    {
        var value = account.OpeningBalance;
        foreach (var item in transactions.Where(x => x.AccountId == account.Id || x.Type == TransactionType.Transfer && x.TransferAccountId == account.Id))
            value += item.Type switch
            {
                TransactionType.Income when item.AccountId == account.Id => item.Amount,
                TransactionType.Expense when item.AccountId == account.Id => -item.Amount,
                TransactionType.Transfer when item.AccountId == account.Id => -item.Amount,
                TransactionType.Transfer when item.TransferAccountId == account.Id => item.Amount,
                _ => 0
            };
        return value;
    }

    public static IReadOnlyDictionary<Guid, decimal> Balances(IEnumerable<Account> accounts, IEnumerable<MoneyTransaction> transactions)
    {
        var balances = accounts.ToDictionary(x => x.Id, x => x.OpeningBalance);
        foreach (var item in transactions)
        {
            switch (item.Type)
            {
                case TransactionType.Income:
                    if (balances.TryGetValue(item.AccountId, out var incomeBalance)) balances[item.AccountId] = incomeBalance + item.Amount;
                    break;
                case TransactionType.Expense:
                    if (balances.TryGetValue(item.AccountId, out var expenseBalance)) balances[item.AccountId] = expenseBalance - item.Amount;
                    break;
                case TransactionType.Transfer:
                    if (balances.TryGetValue(item.AccountId, out var sourceBalance)) balances[item.AccountId] = sourceBalance - item.Amount;
                    if (item.TransferAccountId is { } destination && balances.TryGetValue(destination, out var destinationBalance)) balances[destination] = destinationBalance + item.Amount;
                    break;
            }
        }
        return balances;
    }

    public static DashboardSnapshot BuildSnapshot(IEnumerable<Account> accounts, IEnumerable<MoneyTransaction> transactions,
        IEnumerable<BudgetCategory> categories, DateTime month, decimal committedUpcoming = 0, IEnumerable<BudgetAllocation>? allocations = null)
    {
        var accountList = accounts.ToList();
        var allTransactions = transactions as IReadOnlyList<MoneyTransaction> ?? transactions.ToList();
        var allocationList = allocations?.ToList() ?? [];
        var balances = Balances(accountList, allTransactions);
        var previousMonth = month.AddMonths(-1);
        var currentSpend = new Dictionary<Guid, decimal>();
        var previousSpend = new Dictionary<Guid, decimal>();
        decimal income = 0, expenses = 0;
        foreach (var transaction in allTransactions)
        {
            var inCurrentMonth = transaction.Date.Year == month.Year && transaction.Date.Month == month.Month;
            var inPreviousMonth = transaction.Date.Year == previousMonth.Year && transaction.Date.Month == previousMonth.Month;
            if (inCurrentMonth && transaction.Type == TransactionType.Income) income += transaction.Amount;
            if (inCurrentMonth && transaction.Type == TransactionType.Expense) expenses += transaction.Amount;
            if (transaction.Type != TransactionType.Expense || (!inCurrentMonth && !inPreviousMonth)) continue;
            var target = inCurrentMonth ? currentSpend : previousSpend;
            if (transaction.Splits is { Count: > 0 })
            {
                foreach (var split in transaction.Splits) AddSpend(target, split.CategoryId, split.Amount);
            }
            else if (transaction.CategoryId is { } categoryId) AddSpend(target, categoryId, transaction.Amount);
        }
        var allocationsByMonth = allocationList.ToDictionary(x => (x.CategoryId, x.Year, x.Month));
        var categoryList = categories.OrderBy(x => x.SortOrder).Select(c =>
        {
            allocationsByMonth.TryGetValue((c.Id, month.Year, month.Month), out var allocation);
            var assigned = allocation?.Assigned ?? 0;
            var rollover = allocation?.RolledOver ?? CalculatePreviousMonthRollover(c.Id, allocationsByMonth, previousSpend, previousMonth);
            return new CategoryProgress(c.Id, c.Name, c.Icon, c.Color, c.MonthlyTarget, assigned, rollover,
                currentSpend.GetValueOrDefault(c.Id));
        }).ToList();
        var activeAccounts = accountList.Where(x => !x.IsArchived).ToList();
        var assets = activeAccounts.Where(x => x.Type is not AccountType.Loan).Sum(a => balances[a.Id]);
        var liabilities = activeAccounts.Where(x => x.Type == AccountType.Loan).Sum(a => Math.Abs(balances[a.Id]));
        var rate = income <= 0 ? 0 : Math.Round((income - expenses) / income * 100, 1);
        return new(assets - liabilities, income, expenses, categoryList.Sum(x => x.Assigned), Math.Max(0, income - expenses - committedUpcoming), rate, categoryList);
    }

    public static ForecastSnapshot BuildForecast(IEnumerable<Account> accounts, IEnumerable<MoneyTransaction> transactions,
        IEnumerable<RecurringBill> bills, DateTime today, int days = 90)
    {
        var transactionList = transactions as IReadOnlyList<MoneyTransaction> ?? transactions.ToList(); var accountList = accounts.Where(x => !x.IsArchived).ToList();
        var accountBalances = Balances(accountList, transactionList);
        var starting = accountList.Where(x => x.Type is not AccountType.Loan).Sum(x => accountBalances[x.Id])
            - accountList.Where(x => x.Type == AccountType.Loan).Sum(x => Math.Abs(accountBalances[x.Id]));
        var recentStart = new DateTime(today.Year, today.Month, 1).AddMonths(-3);
        var recentIncome = transactionList.Where(x => x.Type == TransactionType.Income && x.Date >= recentStart && x.Date <= today).Sum(x => x.Amount);
        var monthlyIncome = recentIncome / 3m; var end = today.AddDays(days); var scheduled = 0m;
        foreach (var bill in bills)
        {
            var due = bill.NextDue;
            while (due < today) due = NextOccurrence(due, bill.Frequency);
            while (due <= end) { scheduled += bill.Amount; due = NextOccurrence(due, bill.Frequency); }
        }
        var months = Math.Max(1, (int)Math.Ceiling(days / 30m)); var expectedIncome = monthlyIncome * months;
        var balances = new List<decimal>(months); var running = starting;
        for (var i = 0; i < months; i++)
        {
            var periodStart = today.AddMonths(i); var periodEnd = i == months - 1 ? end : today.AddMonths(i + 1);
            var outflow = BillsBetween(bills, periodStart, periodEnd, today); running += monthlyIncome - outflow; balances.Add(running);
        }
        return new(starting, expectedIncome, scheduled, starting + expectedIncome - scheduled, balances.Prepend(starting).Min(), balances);
    }

    public static decimal MonthlyGoalContribution(SavingsGoal goal, DateTime today)
    {
        var remaining = Math.Max(0, goal.TargetAmount - goal.SavedAmount);
        var months = Math.Max(1, ((goal.TargetDate.Year - today.Year) * 12) + goal.TargetDate.Month - today.Month);
        return Math.Ceiling(remaining / months);
    }

    public static DateTime NextOccurrence(DateTime current, RecurrenceFrequency frequency) => frequency switch
    {
        RecurrenceFrequency.Weekly => current.AddDays(7),
        RecurrenceFrequency.Monthly => current.AddMonths(1),
        RecurrenceFrequency.Quarterly => current.AddMonths(3),
        _ => current.AddYears(1)
    };

    private static void AddSpend(Dictionary<Guid, decimal> spend, Guid categoryId, decimal amount) =>
        spend[categoryId] = spend.GetValueOrDefault(categoryId) + amount;

    private static decimal CalculatePreviousMonthRollover(Guid categoryId,
        IReadOnlyDictionary<(Guid CategoryId, int Year, int Month), BudgetAllocation> allocations,
        IReadOnlyDictionary<Guid, decimal> spend, DateTime previousMonth)
    {
        allocations.TryGetValue((categoryId, previousMonth.Year, previousMonth.Month), out var allocation);
        var available = (allocation?.Assigned ?? 0) + (allocation?.RolledOver ?? 0);
        var spent = spend.GetValueOrDefault(categoryId);
        return Math.Max(0, available - spent);
    }

    private static decimal BillsBetween(IEnumerable<RecurringBill> bills, DateTime start, DateTime end, DateTime today)
    {
        var total = 0m;
        foreach (var bill in bills)
        {
            var due = bill.NextDue; while (due < today) due = NextOccurrence(due, bill.Frequency);
            while (due < end) { if (due >= start) total += bill.Amount; due = NextOccurrence(due, bill.Frequency); }
        }
        return total;
    }
}
