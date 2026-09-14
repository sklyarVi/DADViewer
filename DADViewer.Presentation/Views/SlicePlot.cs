using System.Windows;
using System.Windows.Media;
namespace DADViewer.Presentation.Views;

public sealed class SlicePlot : PlotSurface
{
    private IReadOnlyList<double>? _x, _referenceX;
    private double[]? _y, _referenceY, _baseline;
    private IReadOnlyList<int> _peaks = Array.Empty<int>();
    private int _selected;
    private string _xlabel = "", _title = "";
    private double XMin => Math.Min(_x![0], _referenceX?[0] ?? _x[0]);
    private double XMax => Math.Max(_x![^1], _referenceX?[^1] ?? _x[^1]);
    private double YMin => Math.Min(Math.Min(_y!.Min(), _referenceY?.Min() ?? _y!.Min()), _baseline?.Min() ?? _y!.Min());
    private double YMax => Math.Max(Math.Max(_y!.Max(), _referenceY?.Max() ?? _y!.Max()), _baseline?.Max() ?? _y!.Max());
    public override string VisibleRangeDescription => _x == null || _y == null ? "" : $"{_xlabel}: {XValue(0, XMin, XMax):G6} … {XValue(1, XMin, XMax):G6}; intensity: {YValue(0, YMin, YMax):G6} … {YValue(1, YMin, YMax):G6}";
    public int PointCount => _y?.Length ?? 0;
    public event Action<int>? IndexSelected;
    public void SetSeries(IReadOnlyList<double> x, double[] y, int selected, string xlabel, string title)
    { if (!ReferenceEquals(_x, x)) ResetZoom(); _x = x; _y = y; _selected = selected; _xlabel = xlabel; _title = title; InvalidateVisual(); }
    public void SetAnalysis(double[]? baseline, IReadOnlyList<int>? peaks, IReadOnlyList<double>? referenceX, double[]? referenceY)
    { _baseline = baseline; _peaks = peaks ?? Array.Empty<int>(); _referenceX = referenceX; _referenceY = referenceY; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_x == null || _y == null) { dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize)); Text(dc, "Select a point on the intensity map", 25, 40); return; }
        double xmin = XMin, xmax = XMax, min = YMin, max = YMax; var r = PlotArea;
        Axes(dc, xmin, xmax, min, max, _xlabel, _title);
        Point Map(double x, double y) => new(r.Left + XFraction(x, xmin, xmax) * r.Width, r.Bottom - YFraction(y, min, max) * r.Height);
        void Curve(IReadOnlyList<double> x, double[] y, Pen pen)
        {
            // First/min/max/last per horizontal pixel preserves narrow peaks in both curves.
            var indices = new List<int>(); int start = 0;
            while (start + 1 < y.Length && XFraction(x[start + 1], xmin, xmax) < 0) start++;
            while (start < y.Length && (indices.Count == 0 || Map(x[indices[^1]], y[indices[^1]]).X <= r.Right))
            {
                int end = start + 1, lo = start, hi = start;
                int bucket = (int)Math.Clamp(Map(x[start], y[start]).X, -1e8, 1e8);
                while (end < y.Length && (int)Math.Clamp(Map(x[end], y[end]).X, -1e8, 1e8) == bucket)
                { if (y[end] < y[lo]) lo = end; if (y[end] > y[hi]) hi = end; end++; }
                indices.Add(start);
                if (lo < hi) { indices.Add(lo); indices.Add(hi); } else { indices.Add(hi); indices.Add(lo); }
                indices.Add(end - 1); start = end;
            }
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            { ctx.BeginFigure(Map(x[indices[0]], y[indices[0]]), false, false); foreach (int i in indices) ctx.LineTo(Map(x[i], y[i]), true, false); }
            geometry.Freeze(); dc.DrawGeometry(null, pen, geometry);
        }
        dc.PushClip(new RectangleGeometry(r));
        if (_baseline is { Length: > 0 }) dc.DrawLine(new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dash }, Map(_x[0], _baseline[0]), Map(_x[^1], _baseline[^1]));
        if (_referenceX != null && _referenceY != null) Curve(_referenceX, _referenceY, new Pen(Brushes.DarkOrange, 1.5));
        Curve(_x, _y, new Pen(Brushes.SteelBlue, 1.5));
        foreach (int i in _peaks) if (i >= 0 && i < _y.Length) dc.DrawEllipse(Brushes.Teal, new Pen(Brushes.White, 1), Map(_x[i], _y[i]), 3.5, 3.5);
        if (_selected >= 0 && _selected < _y.Length) dc.DrawEllipse(Brushes.OrangeRed, new Pen(Brushes.White, 1), Map(_x[_selected], _y[_selected]), 4, 4);
        dc.Pop(); DrawInteraction(dc);
    }
    protected override void PointChosen(Point point)
    {
        if (_x == null || !PlotArea.Contains(point)) return;
        var r = PlotArea; double value = XValue((point.X - r.Left) / r.Width, XMin, XMax);
        int low = 0, high = _x.Count - 1;
        while (low < high) { int mid = (low + high) / 2; if (_x[mid] < value) low = mid + 1; else high = mid; }
        if (low > 0 && value - _x[low - 1] <= _x[low] - value) low--;
        IndexSelected?.Invoke(low);
    }
}
