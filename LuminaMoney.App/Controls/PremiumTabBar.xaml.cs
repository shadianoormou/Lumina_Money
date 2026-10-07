namespace LuminaMoney.App.Controls;

public partial class PremiumTabBar : ContentView
{
    public static readonly BindableProperty ActiveTabProperty = BindableProperty.Create(
        nameof(ActiveTab), typeof(string), typeof(PremiumTabBar), "Home", propertyChanged: OnActiveTabChanged);

    public string ActiveTab { get => (string)GetValue(ActiveTabProperty); set => SetValue(ActiveTabProperty, value); }

    public PremiumTabBar()
    {
        InitializeComponent();
        UpdateSelection();
    }

    private static void OnActiveTabChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((PremiumTabBar)bindable).UpdateSelection();

    private void UpdateSelection()
    {
        if (HomeItem is null) return;
        BarShell.BackgroundColor = Color.FromArgb("#0E1623");
        BarShell.Stroke = Color.FromArgb("#27374F");
        SetSelected(HomeItem, HomeIcon, HomeLabel, "Home");
        SetSelected(BudgetItem, BudgetIcon, BudgetLabel, "Budget");
        SetSelected(ActivityItem, ActivityIcon, ActivityLabel, "Activity");
        SetSelected(PlanItem, PlanIcon, PlanLabel, "Plan");
        SetSelected(AccountsItem, AccountsIcon, AccountsLabel, "Accounts");
    }

    private void SetSelected(Border item, Image icon, Label label, string tab)
    {
        var selected = string.Equals(ActiveTab, tab, StringComparison.OrdinalIgnoreCase);
        item.BackgroundColor = selected ? Color.FromArgb("#17365C") : Colors.Transparent;
        item.Stroke = selected ? Color.FromArgb("#2D5788") : Colors.Transparent;
        item.StrokeThickness = selected ? 1 : 0;
        icon.Opacity = selected ? 1 : .52;
        label.TextColor = selected
            ? Color.FromArgb("#9BC4FF")
            : Color.FromArgb("#8393AB");
        label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        if (Preferences.Default.Get("lumina.reduced_motion", false))
        {
            item.Scale = selected ? 1.025 : 1;
            icon.Scale = selected ? 1.08 : 1;
            icon.TranslationY = selected ? -1 : 0;
            label.Opacity = selected ? 1 : .76;
            return;
        }
        _ = item.ScaleToAsync(selected ? 1.025 : 1, 230, Easing.SpringOut);
        _ = icon.ScaleToAsync(selected ? 1.08 : 1, 260, Easing.SpringOut);
        _ = icon.TranslateToAsync(0, selected ? -1 : 0, 220, Easing.CubicOut);
        _ = label.FadeToAsync(selected ? 1 : .76, 200, Easing.CubicOut);
    }

    private async void HomeTapped(object? sender, TappedEventArgs e) => await NavigateAsync("Home", "dashboard");
    private async void BudgetTapped(object? sender, TappedEventArgs e) => await NavigateAsync("Budget", "budget");
    private async void ActivityTapped(object? sender, TappedEventArgs e) => await NavigateAsync("Activity", "transactions");
    private async void PlanTapped(object? sender, TappedEventArgs e) => await NavigateAsync("Plan", "plan");
    private async void AccountsTapped(object? sender, TappedEventArgs e) => await NavigateAsync("Accounts", "accounts");

    private async Task NavigateAsync(string tab, string route)
    {
        if (string.Equals(ActiveTab, tab, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.Click);
            await Shell.Current.GoToAsync($"//main/{route}", true);
        }
        catch (FeatureNotSupportedException) { await Shell.Current.GoToAsync($"//main/{route}"); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { }
    }
}
