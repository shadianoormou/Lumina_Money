namespace LuminaMoney.App.Controls;

public static class PremiumMotion
{
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled", typeof(bool), typeof(PremiumMotion), false, propertyChanged: OnEnabledChanged);

    public static bool GetIsEnabled(BindableObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static void OnEnabledChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view || newValue is not true) return;
        if (view is Button button)
        {
            button.Pressed += ButtonPressed;
            button.Released += ButtonReleased;
            return;
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += ViewTapped;
        view.GestureRecognizers.Add(tap);
    }

    private static async void ButtonPressed(object? sender, EventArgs e)
    {
        if (sender is not Button button) return;
        PerformHaptic();
        if (ReduceMotion) return;
        await AnimateScaleAsync(button, .965, 90, Easing.CubicOut);
    }

    private static async void ButtonReleased(object? sender, EventArgs e)
    {
        if (sender is not Button button || ReduceMotion) return;
        await AnimateScaleAsync(button, 1, 260, Easing.SpringOut);
    }

    private static async void ViewTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TapGestureRecognizer gesture || gesture.Parent is not View view) return;
        PerformHaptic();
        if (ReduceMotion) return;
        try
        {
            await Task.WhenAll(view.ScaleToAsync(.982, 90, Easing.CubicOut), view.TranslateToAsync(0, -2, 90, Easing.CubicOut));
            await Task.WhenAll(view.ScaleToAsync(1, 300, Easing.SpringOut), view.TranslateToAsync(0, 0, 260, Easing.CubicOut));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private static async Task AnimateScaleAsync(VisualElement view, double scale, uint duration, Easing easing)
    {
        try { await view.ScaleToAsync(scale, duration, easing); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private static bool ReduceMotion => Preferences.Default.Get("lumina.reduced_motion", false);

    private static void PerformHaptic()
    {
        try { HapticFeedback.Default.Perform(HapticFeedbackType.Click); }
        catch (FeatureNotSupportedException) { }
        catch (PermissionException) { }
    }
}
