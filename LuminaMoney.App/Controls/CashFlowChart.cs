namespace LuminaMoney.App.Controls;
public sealed class CashFlowChart : GraphicsView, IDrawable
{
 public static readonly BindableProperty ValuesProperty=BindableProperty.Create(nameof(Values),typeof(IReadOnlyList<decimal>),typeof(CashFlowChart),Array.Empty<decimal>(),propertyChanged:(b,o,n)=>((CashFlowChart)b).Restart());
 public IReadOnlyList<decimal> Values{get=>(IReadOnlyList<decimal>)GetValue(ValuesProperty);set=>SetValue(ValuesProperty,value);}
 private float _reveal; private int _selectedIndex=-1; private int _animationGeneration;
 public CashFlowChart(){Drawable=this;HeightRequest=172;StartInteraction+=(_,e)=>SelectAt(e.Touches.FirstOrDefault().X);DragInteraction+=(_,e)=>SelectAt(e.Touches.FirstOrDefault().X);EndInteraction+=(_,_)=>{Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(900),()=>{_selectedIndex=-1;Invalidate();});};}
 protected override void OnHandlerChanged(){base.OnHandlerChanged();Restart();}
 private void Restart(){var generation=++_animationGeneration;Invalidate();if(Handler is null)return;if(Preferences.Default.Get("lumina.reduced_motion",false)){_reveal=1;Invalidate();return;}_reveal=0;Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16),()=>{if(generation!=_animationGeneration||Handler is null)return false;_reveal=Math.Min(1,_reveal+.04f);Invalidate();return _reveal<1;});}
 public void Draw(ICanvas canvas,RectF dirty)
 {
  var values=Values;if(values.Count<2)return;var pad=12f;var width=dirty.Width-pad*2;var height=dirty.Height-pad*2;var min=Math.Min(0,values.Min());var max=Math.Max(0,values.Max());if(max-min<1){var center=(max+min)/2;min=center-1;max=center+1;}var range=max-min;
  var zeroY=pad+height-(float)((0-min)/range)*height;
  canvas.StrokeColor=Color.FromArgb("#26364D");canvas.StrokeSize=1;for(var i=1;i<4;i++){var y=pad+height*i/4;if(Math.Abs(y-zeroY)>2)canvas.DrawLine(pad,y,dirty.Width-pad,y);}canvas.StrokeColor=Color.FromArgb("#405471");canvas.DrawLine(pad,zeroY,dirty.Width-pad,zeroY);
  var count=Math.Max(2,(int)Math.Ceiling(values.Count*_reveal));var line=new PathF();var fill=new PathF();
  for(var i=0;i<count&&i<values.Count;i++){var x=pad+width*i/(values.Count-1);var y=pad+height-(float)((values[i]-min)/range)*height;if(i==0){line.MoveTo(x,y);fill.MoveTo(x,zeroY);fill.LineTo(x,y);}else{line.LineTo(x,y);fill.LineTo(x,y);}}
  var lastIndex=Math.Min(count,values.Count)-1;var lastX=pad+width*lastIndex/(values.Count-1);fill.LineTo(lastX,zeroY);fill.Close();canvas.FillColor=Color.FromRgba(91,150,255,24);canvas.FillPath(fill);
  canvas.StrokeColor=Color.FromArgb("#72AAFF");canvas.StrokeSize=3f;canvas.StrokeLineCap=LineCap.Round;canvas.StrokeLineJoin=LineJoin.Round;canvas.DrawPath(line);
  if(_reveal>=1){var endY=pad+height-(float)((values[^1]-min)/range)*height;canvas.FillColor=Color.FromRgba(102,163,255,42);canvas.FillCircle(lastX,endY,9);canvas.FillColor=Color.FromArgb("#8DBAFF");canvas.FillCircle(lastX,endY,4.5f);}
  if(_selectedIndex>=0&&_selectedIndex<values.Count){var x=pad+width*_selectedIndex/(values.Count-1);var y=pad+height-(float)((values[_selectedIndex]-min)/range)*height;canvas.StrokeColor=Color.FromArgb("#8293AA");canvas.StrokeSize=1;canvas.DrawLine(x,pad,x,dirty.Height-pad);canvas.FillColor=Color.FromArgb("#16243A");canvas.FillCircle(x,y,6);var label=$"{(values[_selectedIndex]>=0?"+":"−")}£{Math.Abs(values[_selectedIndex]):N0}";var bubble=new RectF(Math.Clamp(x-42,0,dirty.Width-84),Math.Max(0,y-39),84,29);canvas.FillColor=Color.FromArgb("#16243A");canvas.FillRoundedRectangle(bubble,10);canvas.FontColor=Color.FromArgb("#EAF1FB");canvas.FontSize=11;canvas.Font=Microsoft.Maui.Graphics.Font.DefaultBold;canvas.DrawString(label,bubble,HorizontalAlignment.Center,VerticalAlignment.Center);}
 }
 private void SelectAt(float x){if(Values.Count==0||Width<=0)return;_selectedIndex=Math.Clamp((int)Math.Round((x-12)/Math.Max(1,Width-24)*(Values.Count-1)),0,Values.Count-1);Invalidate();try{HapticFeedback.Default.Perform(HapticFeedbackType.Click);}catch(FeatureNotSupportedException){}catch(PermissionException){}}
}
