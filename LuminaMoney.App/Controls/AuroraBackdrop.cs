namespace LuminaMoney.App.Controls;

public sealed class AuroraBackdrop : GraphicsView, IDrawable
{
    private double _phase;
    private int _generation;

    public AuroraBackdrop()
    {
        Drawable = this;
        InputTransparent = true;
        Opacity = .92;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is null) { _generation++; return; }
        StartReveal();
    }

    private void StartReveal()
    {
        var generation = ++_generation;
        if (Preferences.Default.Get("lumina.reduced_motion", false))
        {
            _phase = .38;
            Invalidate();
            return;
        }

        _phase = 0;
        var frames = 0;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(33), () =>
        {
            if (generation != _generation || Handler is null) return false;
            _phase += .022;
            frames++;
            Invalidate();
            return frames < 150;
        });
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.SaveState();
        var clip = new PathF();
        clip.AppendRoundedRectangle(dirtyRect, Math.Min(dirtyRect.Width, dirtyRect.Height) * .12f);
        canvas.ClipPath(clip);

        var drift = (float)Math.Sin(_phase * Math.PI * 2);
        var breathe = (float)Math.Cos(_phase * Math.PI * 1.35);
        canvas.FillColor = Color.FromRgba(255, 255, 255, 18);
        canvas.FillCircle(dirtyRect.Width * (.83f + drift * .045f), dirtyRect.Height * .12f, dirtyRect.Width * .34f);
        canvas.FillColor = Color.FromRgba(130, 230, 215, 24);
        canvas.FillCircle(dirtyRect.Width * (.14f - drift * .035f), dirtyRect.Height * (.88f + breathe * .025f), dirtyRect.Width * .43f);
        canvas.FillColor = Color.FromRgba(110, 162, 255, 22);
        canvas.FillCircle(dirtyRect.Width * (.72f - breathe * .035f), dirtyRect.Height * .88f, dirtyRect.Width * .31f);

        canvas.StrokeColor = Color.FromRgba(255, 255, 255, 32);
        canvas.StrokeSize = 1;
        for (var i = 0; i < 3; i++)
        {
            var y = dirtyRect.Height * (.24f + i * .22f) + drift * 4;
            canvas.DrawLine(dirtyRect.Width * .62f, y, dirtyRect.Width * .95f, y - 18);
        }
        canvas.RestoreState();
    }
}
