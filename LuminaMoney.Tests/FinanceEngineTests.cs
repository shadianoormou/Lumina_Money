using LuminaMoney.Core;

namespace LuminaMoney.Tests;

public sealed class FinanceEngineTests
{
    [Fact]
    public void Snapshot_calculates_cashflow_budget_and_networth()
    {
        var account = new Account(Guid.NewGuid(), "Main", AccountType.Current, "GBP", 1000, "#000");
        var category = new BudgetCategory(Guid.NewGuid(), "Food", "F", "#000", 400);
        var transactions = new[]
        {
            new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 8, 1), 3000, TransactionType.Income, "Salary"),
            new MoneyTransaction(Guid.NewGuid(), account.Id, category.Id, new(2026, 8, 2), 250, TransactionType.Expense, "Market")
        };

        var allocation = new BudgetAllocation(Guid.NewGuid(), category.Id, 2026, 8, 400);
        var result = FinanceEngine.BuildSnapshot([account], transactions, [category], new(2026, 8, 1), 100, [allocation]);
        Assert.Equal(3750, result.NetWorth);
        Assert.Equal(2650, result.SafeToSpend);
        Assert.Equal(150, result.Categories[0].Remaining);
        Assert.Equal(400, result.Budgeted);
    }

    [Fact]
    public void Monthly_goal_contribution_never_divides_by_zero()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Trip", 1200, 300, new(2026, 8, 25), "P", "#000");
        Assert.Equal(900, FinanceEngine.MonthlyGoalContribution(goal, new(2026, 8, 24)));
    }

    [Fact]
    public void Snapshot_excludes_other_months_from_monthly_cashflow()
    {
        var account = new Account(Guid.NewGuid(), "Main", AccountType.Current, "GBP", 0, "#000");
        var transactions = new[]
        {
            new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 7, 31), 900, TransactionType.Expense, "Last month"),
            new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 8, 1), 1500, TransactionType.Income, "Salary")
        };

        var result = FinanceEngine.BuildSnapshot([account], transactions, [], new(2026, 8, 20));
        Assert.Equal(1500, result.Income);
        Assert.Equal(0, result.Expenses);
    }

    [Fact]
    public void Loan_balance_is_treated_as_a_liability()
    {
        var cash = new Account(Guid.NewGuid(), "Cash", AccountType.Current, "GBP", 5000, "#000");
        var loan = new Account(Guid.NewGuid(), "Loan", AccountType.Loan, "GBP", -1200, "#000");

        var result = FinanceEngine.BuildSnapshot([cash, loan], [], [], new(2026, 8, 20));
        Assert.Equal(3800, result.NetWorth);
    }

    [Fact]
    public void Archived_accounts_are_excluded_from_active_net_worth()
    {
        var active = new Account(Guid.NewGuid(), "Main", AccountType.Current, "GBP", 5000, "#000");
        var archived = new Account(Guid.NewGuid(), "Old savings", AccountType.Savings, "GBP", 1200, "#000", true);

        var result = FinanceEngine.BuildSnapshot([active, archived], [], [], new(2026, 8, 20));

        Assert.Equal(5000, result.NetWorth);
    }

    [Fact]
    public void Unspent_envelope_money_rolls_into_the_next_month()
    {
        var account = new Account(Guid.NewGuid(), "Main", AccountType.Current, "GBP", 0, "#000");
        var category = new BudgetCategory(Guid.NewGuid(), "Food", "F", "#000", 500);
        var julyAllocation = new BudgetAllocation(Guid.NewGuid(), category.Id, 2026, 7, 500);
        var julySpend = new MoneyTransaction(Guid.NewGuid(), account.Id, category.Id, new(2026, 7, 10), 200, TransactionType.Expense, "Market");

        var result = FinanceEngine.BuildSnapshot([account], [julySpend], [category], new(2026, 8, 1), allocations: [julyAllocation]);

        Assert.Equal(300, result.Categories[0].RolledOver);
        Assert.Equal(300, result.Categories[0].Remaining);
    }

    [Fact]
    public void Forecast_combines_average_income_with_recurring_commitments()
    {
        var account = new Account(Guid.NewGuid(), "Main", AccountType.Current, "GBP", 1000, "#000");
        var transactions = new[]
        {
            new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 6, 1), 2000, TransactionType.Income, "Salary"),
            new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 7, 1), 2000, TransactionType.Income, "Salary"),
            new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 8, 1), 2000, TransactionType.Income, "Salary")
        };
        var bill = new RecurringBill(Guid.NewGuid(), "Rent", 500, new(2026, 9, 1), RecurrenceFrequency.Monthly, null);

        var result = FinanceEngine.BuildForecast([account], transactions, [bill], new(2026, 8, 25), 90);

        Assert.Equal(7000, result.StartingBalance);
        Assert.Equal(6000, result.ExpectedIncome);
        Assert.Equal(1500, result.ScheduledOutflow);
        Assert.Equal(11500, result.ProjectedBalance);
        Assert.True(result.StaysPositive);
    }

    [Theory]
    [InlineData(RecurrenceFrequency.Weekly, "2026-09-01")]
    [InlineData(RecurrenceFrequency.Monthly, "2026-09-25")]
    [InlineData(RecurrenceFrequency.Quarterly, "2026-11-25")]
    [InlineData(RecurrenceFrequency.Yearly, "2027-08-25")]
    public void Recurrence_calculates_the_next_due_date(RecurrenceFrequency frequency, string expected)
    {
        Assert.Equal(DateTime.Parse(expected), FinanceEngine.NextOccurrence(new(2026, 8, 25), frequency));
    }

    [Fact]
    public void Transfer_debits_source_and_credits_destination()
    {
        var source = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 1000, "#000");
        var destination = new Account(Guid.NewGuid(), "Savings", AccountType.Savings, "GBP", 200, "#000");
        var transfer = new MoneyTransaction(Guid.NewGuid(), source.Id, null, new(2026, 8, 20), 300, TransactionType.Transfer, "Transfer to Savings", TransferAccountId: destination.Id);

        Assert.Equal(700, FinanceEngine.Balance(source, [transfer]));
        Assert.Equal(500, FinanceEngine.Balance(destination, [transfer]));
    }

    [Fact]
    public void Balances_projects_many_accounts_in_one_pass_and_preserves_transfer_semantics()
    {
        var current = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 1000, "#000");
        var savings = new Account(Guid.NewGuid(), "Savings", AccountType.Savings, "GBP", 200, "#000");
        var loan = new Account(Guid.NewGuid(), "Loan", AccountType.Loan, "GBP", -900, "#000");
        var transactions = new[]
        {
            new MoneyTransaction(Guid.NewGuid(), current.Id, null, DateTime.Today, 300, TransactionType.Income, "Pay"),
            new MoneyTransaction(Guid.NewGuid(), current.Id, null, DateTime.Today, 100, TransactionType.Expense, "Shop"),
            new MoneyTransaction(Guid.NewGuid(), current.Id, null, DateTime.Today, 250, TransactionType.Transfer, "Save", TransferAccountId: savings.Id),
            new MoneyTransaction(Guid.NewGuid(), current.Id, null, DateTime.Today, 100, TransactionType.Transfer, "Loan payment", TransferAccountId: loan.Id)
        };

        var result = FinanceEngine.Balances([current, savings, loan], transactions);

        Assert.Equal(850, result[current.Id]);
        Assert.Equal(450, result[savings.Id]);
        Assert.Equal(-800, result[loan.Id]);
    }

    [Fact]
    public void Transfer_does_not_change_combined_net_worth_or_cashflow()
    {
        var source = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 1000, "#000");
        var destination = new Account(Guid.NewGuid(), "Savings", AccountType.Savings, "GBP", 200, "#000");
        var transfer = new MoneyTransaction(Guid.NewGuid(), source.Id, null, new(2026, 8, 20), 300, TransactionType.Transfer, "Transfer", TransferAccountId: destination.Id);

        var result = FinanceEngine.BuildSnapshot([source, destination], [transfer], [], new(2026, 8, 1));

        Assert.Equal(1200, result.NetWorth);
        Assert.Equal(0, result.Income);
        Assert.Equal(0, result.Expenses);
    }

    [Fact]
    public void Split_expense_is_allocated_to_each_envelope()
    {
        var account = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 1000, "#000");
        var food = new BudgetCategory(Guid.NewGuid(), "Food", "F", "#000", 300);
        var home = new BudgetCategory(Guid.NewGuid(), "Home", "H", "#000", 300);
        var splits = new[] { new TransactionSplit(Guid.NewGuid(), food.Id, 70), new TransactionSplit(Guid.NewGuid(), home.Id, 30) };
        var expense = new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 8, 20), 100, TransactionType.Expense, "Superstore", Splits: splits);
        var allocations = new[] { new BudgetAllocation(Guid.NewGuid(), food.Id, 2026, 8, 200), new BudgetAllocation(Guid.NewGuid(), home.Id, 2026, 8, 200) };

        var result = FinanceEngine.BuildSnapshot([account], [expense], [food, home], new(2026, 8, 1), allocations: allocations);

        Assert.Equal(70, result.Categories.Single(x => x.CategoryId == food.Id).Spent);
        Assert.Equal(30, result.Categories.Single(x => x.CategoryId == home.Id).Spent);
        Assert.Equal(100, result.Expenses);
    }

    [Fact]
    public void Split_expense_reduces_account_balance_once()
    {
        var account = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 1000, "#000");
        var splits = new[] { new TransactionSplit(Guid.NewGuid(), Guid.NewGuid(), 60), new TransactionSplit(Guid.NewGuid(), Guid.NewGuid(), 40) };
        var expense = new MoneyTransaction(Guid.NewGuid(), account.Id, null, DateTime.Today, 100, TransactionType.Expense, "Store", Splits: splits);

        Assert.Equal(900, FinanceEngine.Balance(account, [expense]));
    }

    [Fact]
    public void Split_spending_is_used_when_calculating_rollover()
    {
        var account = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 0, "#000");
        var category = new BudgetCategory(Guid.NewGuid(), "Home", "H", "#000", 500);
        var allocation = new BudgetAllocation(Guid.NewGuid(), category.Id, 2026, 7, 500);
        var splits = new[] { new TransactionSplit(Guid.NewGuid(), category.Id, 125), new TransactionSplit(Guid.NewGuid(), Guid.NewGuid(), 75) };
        var expense = new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 7, 10), 200, TransactionType.Expense, "Store", Splits: splits);

        var result = FinanceEngine.BuildSnapshot([account], [expense], [category], new(2026, 8, 1), allocations: [allocation]);

        Assert.Equal(375, result.Categories[0].RolledOver);
    }

    [Fact]
    public void Over_budget_envelope_reports_negative_remaining()
    {
        var account = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 0, "#000");
        var category = new BudgetCategory(Guid.NewGuid(), "Food", "F", "#000", 100);
        var allocation = new BudgetAllocation(Guid.NewGuid(), category.Id, 2026, 8, 100);
        var expense = new MoneyTransaction(Guid.NewGuid(), account.Id, category.Id, new(2026, 8, 2), 140, TransactionType.Expense, "Market");

        var result = FinanceEngine.BuildSnapshot([account], [expense], [category], new(2026, 8, 1), allocations: [allocation]).Categories[0];

        Assert.True(result.IsOverBudget);
        Assert.Equal(-40, result.Remaining);
        Assert.Equal(1, result.Progress);
    }

    [Fact]
    public void Safe_to_spend_never_shows_a_negative_amount()
    {
        var account = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 0, "#000");
        var expense = new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 8, 2), 500, TransactionType.Expense, "Emergency");

        var result = FinanceEngine.BuildSnapshot([account], [expense], [], new(2026, 8, 1), committedUpcoming: 100);

        Assert.Equal(0, result.SafeToSpend);
    }

    [Fact]
    public void Balance_ignores_transactions_from_another_account()
    {
        var account = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 100, "#000");
        var other = new Account(Guid.NewGuid(), "Other", AccountType.Current, "GBP", 0, "#000");
        var income = new MoneyTransaction(Guid.NewGuid(), other.Id, null, DateTime.Today, 500, TransactionType.Income, "Salary");

        Assert.Equal(100, FinanceEngine.Balance(account, [income]));
    }

    [Fact]
    public void Completed_goal_requires_no_more_monthly_contribution()
    {
        var goal = new SavingsGoal(Guid.NewGuid(), "Emergency fund", 5000, 5000, DateTime.Today.AddMonths(6), "G", "#000");

        Assert.Equal(0, FinanceEngine.MonthlyGoalContribution(goal, DateTime.Today));
    }

    [Fact]
    public void Forecast_ignores_future_dated_income_when_estimating_average()
    {
        var account = new Account(Guid.NewGuid(), "Current", AccountType.Current, "GBP", 0, "#000");
        var futureIncome = new MoneyTransaction(Guid.NewGuid(), account.Id, null, new(2026, 9, 1), 9000, TransactionType.Income, "Future salary");

        var result = FinanceEngine.BuildForecast([account], [futureIncome], [], new(2026, 8, 25), 90);

        Assert.Equal(0, result.ExpectedIncome);
    }

    [Fact]
    public void Archived_account_transactions_do_not_leak_into_active_net_worth()
    {
        var archived = new Account(Guid.NewGuid(), "Old", AccountType.Current, "GBP", 1000, "#000", true);
        var income = new MoneyTransaction(Guid.NewGuid(), archived.Id, null, DateTime.Today, 500, TransactionType.Income, "Old income");

        var result = FinanceEngine.BuildSnapshot([archived], [income], [], DateTime.Today);

        Assert.Equal(0, result.NetWorth);
    }
}
