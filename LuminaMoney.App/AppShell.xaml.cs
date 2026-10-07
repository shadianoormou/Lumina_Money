namespace LuminaMoney.App;
public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("transaction-editor", typeof(Pages.TransactionEditorPage));
        Routing.RegisterRoute("account-editor", typeof(Pages.AccountEditorPage));
        Routing.RegisterRoute("budget-editor", typeof(Pages.BudgetEditorPage));
        Routing.RegisterRoute("goal-editor", typeof(Pages.GoalEditorPage));
        Routing.RegisterRoute("bill-editor", typeof(Pages.BillEditorPage));
        Routing.RegisterRoute("budget-move", typeof(Pages.BudgetMovePage));
        Routing.RegisterRoute("budget-assign", typeof(Pages.BudgetAssignPage));
        Routing.RegisterRoute("goal-fund", typeof(Pages.GoalFundPage));
        Routing.RegisterRoute("reports", typeof(Pages.ReportsPage));
        Routing.RegisterRoute("settings", typeof(Pages.SettingsPage));
        Routing.RegisterRoute("membership", typeof(Pages.MembershipPage));
    }
}
