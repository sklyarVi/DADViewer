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
    public override string VisibleRangeDescription => _x == null || _y == null ? "" : $"{_xlabel}: {XValue(0, _x[0], _x[^1]):G6} … {XValue(1, _x[0], _x[^1]):G6}; intensity: {YValue(0, _y.Min(), _y.Max()):G6} … {YValue(1, _y.Min(), _y.Max()):G6}";
    public int PointCount => _y?.Length ?? 0;
    public event Action<int>? IndexSelected;
    public void SetSeries(IReadOnlyList<double> x, double[] y, int selected, string xlabel, string title)
    { if (!ReferenceEquals(_x, x)) ResetZoom(); _x = x; _y = y; _selected = selected; _xlabel = xlabel; _title = title; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_x == null || _y == null) { dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize)); Text(dc, "Select a point on the intensity map", 25, 40); return; }
        double min = _y.Min(), max = _y.Max();
        // Constant signals use Fraction = 0.5, avoiding overflow from artificial padding.
        var r = PlotArea;
        Axes(dc, _x[0], _x[^1], min, max, _xlabel, _title);
        Point Map(int i) => new(r.Left + XFraction(_x[i], _x[0], _x[^1]) * r.Width, r.Bottom - YFraction(_y[i], min, max) * r.Height);
        // Preserve extrema in each horizontal pixel bucket instead of dropping narrow peaks.
        var indices = new List<int>(); int start = 0;
        while (start + 1 < _y.Length && XFraction(_x[start + 1], _x[0], _x[^1]) < 0) start++;
        while (start < _y.Length && (indices.Count == 0 || Map(indices[^1]).X <= r.Right))
        {
            int end = start + 1, lo = start, hi = start;
            int bucket = (int)Math.Clamp(Map(start).X, -1e8, 1e8);
            while (end < _y.Length && (int)Math.Clamp(Map(end).X, -1e8, 1e8) == bucket)
            { if (_y[end] < _y[lo]) lo = end; if (_y[end] > _y[hi]) hi = end; end++; }
            indices.Add(start);
            if (lo < hi) { indices.Add(lo); indices.Add(hi); } else { indices.Add(hi); indices.Add(lo); }
            indices.Add(end - 1); start = end;
        }
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        { ctx.BeginFigure(Map(indices[0]), false, false); foreach (int index in indices) ctx.LineTo(Map(index), true, false); }
        geometry.Freeze();
        dc.PushClip(new RectangleGeometry(r));
        dc.DrawGeometry(null, new Pen(Brushes.SteelBlue, 1.5), geometry);
        if (_selected >= 0 && _selected < _y.Length) dc.DrawEllipse(Brushes.OrangeRed, new Pen(Brushes.White, 1), Map(_selected), 4, 4);
        dc.Pop(); DrawInteraction(dc);
    }
    protected override void PointChosen(Point point)
    {

        if (_x == null || !PlotArea.Contains(point)) return;
        var r = PlotArea; double value = XValue((point.X - r.Left) / r.Width, _x[0], _x[^1]);
        int low = 0, high = _x.Count - 1;
        while (low < high) { int mid = (low + high) / 2; if (_x[mid] < value) low = mid + 1; else high = mid; }
        if (low > 0 && value - _x[low - 1] <= _x[low] - value) low--;
        IndexSelected?.Invoke(low);
    }
}
