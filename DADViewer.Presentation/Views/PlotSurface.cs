using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace DADViewer.Presentation.Views;

public abstract class PlotSurface : FrameworkElement
{
    private Rect _viewport = new(0, 0, 1, 1), _dragViewport;
    private Point? _dragStart;
    private Point _dragEnd;
    private bool _rectangle;
    public Rect Viewport => _viewport;
    public abstract string VisibleRangeDescription { get; }
    public event EventHandler? ViewportChanged;
    protected Rect PlotArea => new(78, 28, Math.Max(1, ActualWidth - 104), Math.Max(1, ActualHeight - 78));
    protected static double Fraction(double value, double min, double max) => max == min ? 0.5 : (value - min) / (max - min);
    protected double XFraction(double value, double min, double max) => (Fraction(value, min, max) - Viewport.X) / Viewport.Width;
    protected double YFraction(double value, double min, double max) => (Fraction(value, min, max) - Viewport.Y) / Viewport.Height;
    protected double XValue(double fraction, double min, double max) => min + (Viewport.X + fraction * Viewport.Width) * (max - min);
    protected double YValue(double fraction, double min, double max) => min + (Viewport.Y + fraction * Viewport.Height) * (max - min);
    public void SetViewport(Rect value)
    {
        if (value.IsEmpty || !double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Width) || !double.IsFinite(value.Height) || value.Width <= 0 || value.Height <= 0) return;
        double w = Math.Clamp(value.Width, 0.000001, 1), h = Math.Clamp(value.Height, 0.000001, 1);
        value = new Rect(Math.Clamp(value.X, 0, 1 - w), Math.Clamp(value.Y, 0, 1 - h), w, h);
        if (value == _viewport) return;
        _viewport = value; InvalidateVisual(); ViewportChanged?.Invoke(this, EventArgs.Empty);
    }
    public void ResetZoom() => SetViewport(new Rect(0, 0, 1, 1));
    protected virtual void PointChosen(Point point) { }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!PlotArea.Contains(e.GetPosition(this))) return;
        var p = e.GetPosition(this); var r = PlotArea;
        double x = (p.X - r.Left) / r.Width, y = (r.Bottom - p.Y) / r.Height;
        double factor = e.Delta > 0 ? 0.8 : 1.25;
        double w = Math.Clamp(Viewport.Width * factor, 0.000001, 1), h = Math.Clamp(Viewport.Height * factor, 0.000001, 1);
        SetViewport(new Rect(Viewport.X + x * (Viewport.Width - w), Viewport.Y + y * (Viewport.Height - h), w, h));
        e.Handled = true;
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var p = e.GetPosition(this); if (!PlotArea.Contains(p)) return;
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) { ResetZoom(); e.Handled = true; return; }
        _rectangle = e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (_rectangle || e.ChangedButton is MouseButton.Right or MouseButton.Middle)
        { _dragStart = _dragEnd = p; _dragViewport = Viewport; CaptureMouse(); e.Handled = true; }
        else if (e.ChangedButton == MouseButton.Left) { PointChosen(p); e.Handled = true; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start) return;
        var p = e.GetPosition(this); var r = PlotArea;
        _dragEnd = new Point(Math.Clamp(p.X, r.Left, r.Right), Math.Clamp(p.Y, r.Top, r.Bottom));
        if (_rectangle) InvalidateVisual();
        else SetViewport(new Rect(_dragViewport.X - (p.X - start.X) / r.Width * _dragViewport.Width,
            _dragViewport.Y + (p.Y - start.Y) / r.Height * _dragViewport.Height, _dragViewport.Width, _dragViewport.Height));
        e.Handled = true;
    }
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragStart is not { } start) return;
        if (_rectangle)
        {
            var box = new Rect(start, _dragEnd); var r = PlotArea;
            if (box.Width >= 5 && box.Height >= 5)
                SetViewport(new Rect(_dragViewport.X + (box.Left - r.Left) / r.Width * _dragViewport.Width,
                    _dragViewport.Y + (r.Bottom - box.Bottom) / r.Height * _dragViewport.Height,
                    box.Width / r.Width * _dragViewport.Width, box.Height / r.Height * _dragViewport.Height));
        }
        _dragStart = null; ReleaseMouseCapture(); InvalidateVisual(); e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { _dragStart = null; InvalidateVisual(); base.OnLostMouseCapture(e); }
    protected void DrawInteraction(DrawingContext dc)
    {
        if (_rectangle && _dragStart is { } p)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 40, 110, 220)), new Pen(Brushes.RoyalBlue, 1), new Rect(p, _dragEnd));
    }
    protected void Text(DrawingContext dc, string text, double x, double y, Brush? brush = null, double size = 11)
        => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Brushes.DimGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
    public static string FormatTick(double value, double span)
    {
        int digits = span > 0 && value != 0 ? Math.Clamp((int)Math.Ceiling(Math.Log10(Math.Abs(value)) - Math.Log10(span)) + 3, 4, 12) : 4;
        return value.ToString("G" + digits, CultureInfo.CurrentCulture);
    }
    protected void Axes(DrawingContext dc, double xmin, double xmax, double ymin, double ymax, string xlabel, string ylabel)
    {
        var r = PlotArea;
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        Text(dc, ylabel, r.Left, 5, Brushes.Black, 12);
        int ticks = r.Width < 400 ? 2 : 4;
        for (int i = 0; i <= ticks; i++)
        {
            double f = i / (double)ticks, x = r.Left + r.Width * f, y = r.Bottom - r.Height * f;
            dc.DrawLine(new Pen(Brushes.LightGray, 0.5), new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawLine(new Pen(Brushes.LightGray, 0.5), new Point(r.Left, y), new Point(r.Right, y));
            Text(dc, FormatTick(XValue(f, xmin, xmax), (xmax - xmin) * Viewport.Width), Math.Min(x - 14, ActualWidth - 70), r.Bottom + 5, size: 10);
            Text(dc, FormatTick(YValue(f, ymin, ymax), (ymax - ymin) * Viewport.Height), 3, y - 7, size: 10);
        }
        dc.DrawRectangle(null, new Pen(Brushes.SlateGray, 1), r);
        Text(dc, xlabel, r.Left + r.Width / 2 - 35, r.Bottom + 26, Brushes.Black);
    }
}
