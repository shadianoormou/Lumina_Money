using System.Globalization;

namespace LuminaMoney.App.Controls;

public sealed class AnimatedAmountLabel : Label
{
    public static readonly BindableProperty AmountTextProperty = BindableProperty.Create(
        nameof(AmountText), typeof(string), typeof(AnimatedAmountLabel), "—", propertyChanged: OnAmountChanged);

    public string AmountText
    {
        get => (string)GetValue(AmountTextProperty);
        set => SetValue(AmountTextProperty, value);
    }

    private int _generation;
    private decimal _displayed;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is not null) AnimateTo(AmountText);
        else _generation++;
    }

    private static void OnAmountChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((AnimatedAmountLabel)bindable).AnimateTo(newValue as string ?? "—");

    private void AnimateTo(string value)
    {
        if (!TryParse(value, out var target, out var showPlus, out var decimals))
        {
            Text = value;
            return;
        }

        var generation = ++_generation;
        if (Handler is null || Preferences.Default.Get("lumina.reduced_motion", false))
        {
            _displayed = target;
            Text = Format(target, showPlus, decimals);
            return;
        }

        var start = _displayed;
        var frame = 0;
        const int totalFrames = 38;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), () =>
        {
            if (generation != _generation || Handler is null) return false;
            frame++;
            var progress = Math.Min(1d, frame / (double)totalFrames);
            var eased = 1d - Math.Pow(1d - progress, 3d);
            _displayed = start + (target - start) * (decimal)eased;
            Text = Format(_displayed, showPlus, decimals);
            if (frame < totalFrames) return true;
            _displayed = target;
            Text = Format(target, showPlus, decimals);
            return false;
        });
    }

    private static bool TryParse(string source, out decimal value, out bool showPlus, out int decimals)
    {
        showPlus = source.TrimStart().StartsWith('+');
        var decimalPoint = source.LastIndexOf('.');
        decimals = decimalPoint >= 0 ? Math.Clamp(source.Length - decimalPoint - 1, 0, 2) : 0;
        var normalized = source.Replace("£", "", StringComparison.Ordinal)
            .Replace(",", "", StringComparison.Ordinal)
            .Replace("+", "", StringComparison.Ordinal)
            .Replace("−", "-", StringComparison.Ordinal)
            .Trim();
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static string Format(decimal value, bool showPlus, int decimals)
    {
        var sign = value < 0 ? "−" : showPlus && value > 0 ? "+" : "";
        var format = decimals > 0 ? $"N{decimals}" : "N0";
        return $"{sign}£{Math.Abs(value).ToString(format, CultureInfo.GetCultureInfo("en-GB"))}";
    }
}
