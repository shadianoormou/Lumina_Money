namespace LuminaMoney.App.Services;

public static class PageMotion
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Layout, object> AnimatedRoots = new();
    private static int _generation;

    public static async Task EnterAsync(Layout root)
    {
        if (AnimatedRoots.TryGetValue(root, out _)) return;
        AnimatedRoots.Add(root, new object());
        var generation = Interlocked.Increment(ref _generation);
        if (Preferences.Default.Get("lumina.reduced_motion", false))
        {
            foreach (var view in root.Children.OfType<VisualElement>()) { view.Opacity = 1; view.TranslationY = 0; view.Scale = 1; }
            return;
        }
        var children = root.Children.OfType<VisualElement>().Take(8).ToArray();
        foreach (var child in children) { child.Opacity = 0; child.TranslationY = 18; child.Scale = 1; }
        var animations = new List<Task>(children.Length);
        foreach (var child in children)
        {
            animations.Add(AnimateSafelyAsync(child, generation, animations.Count * 52));
        }
        await Task.WhenAll(animations);
    }

    private static async Task AnimateSafelyAsync(VisualElement child, int generation, int delay)
    {
        try
        {
            await Task.Delay(delay);
            if (generation != Volatile.Read(ref _generation)) return;
            await Task.WhenAll(child.FadeToAsync(1, 280, Easing.CubicOut), child.TranslateToAsync(0, 0, 340, Easing.CubicOut));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
