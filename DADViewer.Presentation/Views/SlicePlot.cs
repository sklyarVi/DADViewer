using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace DADViewer.Presentation.Views;

public sealed class SlicePlot : PlotSurface
{
    private IReadOnlyList<double>? _x;
    private double[]? _y;
    private int _selected;
    private string _xlabel = "", _title = "";
    public int PointCount => _y?.Length ?? 0;
    public event Action<int>? IndexSelected;
    public void SetSeries(IReadOnlyList<double> x, double[] y, int selected, string xlabel, string title)
    { _x = x; _y = y; _selected = selected; _xlabel = xlabel; _title = title; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_x == null || _y == null) { dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize)); Text(dc, "Select a point on the intensity map", 25, 40); return; }
        double min = _y.Min(), max = _y.Max();
        // Constant signals use Fraction = 0.5, avoiding overflow from artificial padding.
        var r = PlotArea;
        Axes(dc, _x[0], _x[^1], min, max, _xlabel, _title);
        Point Map(int i) => new(r.Left + Fraction(_x[i], _x[0], _x[^1]) * r.Width, r.Bottom - Fraction(_y[i], min, max) * r.Height);
        // Preserve extrema in each horizontal pixel bucket instead of dropping narrow peaks.
        var indices = new List<int>(); int start = 0;
        while (start < _y.Length)
        {
            int end = start + 1, lo = start, hi = start;
            int bucket = (int)Map(start).X;
            while (end < _y.Length && (int)Map(end).X == bucket)
            { if (_y[end] < _y[lo]) lo = end; if (_y[end] > _y[hi]) hi = end; end++; }
            indices.Add(start);
            if (lo < hi) { indices.Add(lo); indices.Add(hi); } else { indices.Add(hi); indices.Add(lo); }
            indices.Add(end - 1); start = end;
        }
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        { ctx.BeginFigure(Map(0), false, false); foreach (int index in indices) ctx.LineTo(Map(index), true, false); }
        geometry.Freeze();
        dc.PushClip(new RectangleGeometry(r));
        dc.DrawGeometry(null, new Pen(Brushes.SteelBlue, 1.5), geometry);
        if (_selected >= 0 && _selected < _y.Length) dc.DrawEllipse(Brushes.OrangeRed, new Pen(Brushes.White, 1), Map(_selected), 4, 4);
        dc.Pop();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_x == null || !PlotArea.Contains(e.GetPosition(this))) return;
        var r = PlotArea; double value = _x[0] + (e.GetPosition(this).X - r.Left) / r.Width * (_x[^1] - _x[0]);
        int low = 0, high = _x.Count - 1;
        while (low < high) { int mid = (low + high) / 2; if (_x[mid] < value) low = mid + 1; else high = mid; }
        if (low > 0 && value - _x[low - 1] <= _x[low] - value) low--;
        IndexSelected?.Invoke(low);
    }
}
