namespace LuminaMoney.App.Controls;
public sealed class ProgressRing : GraphicsView, IDrawable
{
 public static readonly BindableProperty ProgressProperty=BindableProperty.Create(nameof(Progress),typeof(double),typeof(ProgressRing),0d,propertyChanged:(b,o,n)=>((ProgressRing)b).Restart());
 public static readonly BindableProperty CenterTextProperty=BindableProperty.Create(nameof(CenterText),typeof(string),typeof(ProgressRing),"—");
 public double Progress{get=>(double)GetValue(ProgressProperty);set=>SetValue(ProgressProperty,value);} public string CenterText{get=>(string)GetValue(CenterTextProperty);set=>SetValue(CenterTextProperty,value);}
 private float _shown; private int _animationGeneration; public ProgressRing(){Drawable=this;HeightRequest=104;WidthRequest=104;} protected override void OnHandlerChanged(){base.OnHandlerChanged();Restart();}
 private void Restart(){var generation=++_animationGeneration;if(Preferences.Default.Get("lumina.reduced_motion",false)){_shown=(float)Math.Clamp(Progress,0,1);Invalidate();return;}_shown=0;Invalidate();if(Handler is null)return;var target=(float)Math.Clamp(Progress,0,1);Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16),()=>{if(generation!=_animationGeneration||Handler is null)return false;_shown=Math.Min(target,_shown+.03f);Invalidate();return _shown<target;});}
 public void Draw(ICanvas canvas,RectF dirty){var size=Math.Min(dirty.Width,dirty.Height)-14;var rect=new RectF((dirty.Width-size)/2,(dirty.Height-size)/2,size,size);canvas.StrokeSize=10;canvas.StrokeLineCap=LineCap.Round;canvas.StrokeColor=Color.FromArgb("#293952");canvas.DrawEllipse(rect);canvas.StrokeColor=Color.FromArgb("#5B96FF");if(_shown>=.999f)canvas.DrawEllipse(rect);else if(_shown>0)canvas.DrawArc(rect,-90,-90+360*_shown,false,false);canvas.FontColor=Color.FromArgb("#EAF1FB");canvas.FontSize=25;canvas.Font=Microsoft.Maui.Graphics.Font.DefaultBold;canvas.DrawString(CenterText,rect,HorizontalAlignment.Center,VerticalAlignment.Center);}
}
