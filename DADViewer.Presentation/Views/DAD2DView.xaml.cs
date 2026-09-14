using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DADViewer.Domain;
using DADViewer.Presentation.Rendering;
namespace DADViewer.Presentation.Views;

public sealed class DataPointEventArgs(int timeIndex, int wavelengthIndex) : EventArgs
{
    public int TimeIndex { get; } = timeIndex;
    public int WavelengthIndex { get; } = wavelengthIndex;
}

public sealed class DAD2DView : PlotSurface
{
    private DADData? _data;
    private BitmapSource? _bitmap;
    private int _time, _wave;
    private Rect _imageViewport; public ColorScale? RenderedScale { get; private set; }
    public override string VisibleRangeDescription => _data == null ? "" : $"Time: {XValue(0, _data.TimeStamps[0], _data.TimeStamps[^1]):G6} … {XValue(1, _data.TimeStamps[0], _data.TimeStamps[^1]):G6} min; wavelength: {YValue(0, _data.Wavelengths[0], _data.Wavelengths[^1]):G6} … {YValue(1, _data.Wavelengths[0], _data.Wavelengths[^1]):G6} nm";
    public bool HasImage => _bitmap != null; public bool IsCurrentImage => HasImage && _imageViewport == Viewport;
    public void Clear() { _data = null; _bitmap = null; ResetZoom(); InvalidateVisual(); }
    public event EventHandler<DataPointEventArgs>? DataPointSelected;
    public async Task RenderAsync(DADData data, ColorScale scale, CancellationToken token)
    {
        var viewport = Viewport; // Resample original data for every visible region.
        var bitmap = await Task.Run(() =>
        {
            const int width = 1000, height = 500;
            var pixels = new byte[width * height * 4];
            var times = new int[width]; var waves = new int[height];
            for (int x = 0; x < width; x++) times[x] = data.FindTime(data.TimeStamps[0] + (data.TimeStamps[^1] - data.TimeStamps[0]) * (viewport.X + viewport.Width * (x / (width - 1d))));
            for (int y = 0; y < height; y++) waves[y] = data.FindWavelength(data.Wavelengths[0] + (data.Wavelengths[^1] - data.Wavelengths[0]) * (viewport.Y + viewport.Height * (1 - y / (height - 1d))));
            for (int y = 0; y < height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < width; x++)
                {
                    var c = scale.Map(data.GetIntensity(times[x], waves[y]));
                    int p = (y * width + x) * 4; pixels[p] = c.B; pixels[p + 1] = c.G; pixels[p + 2] = c.R; pixels[p + 3] = 255;
                }
            }
            var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            image.Freeze(); return image;
        }, token);
        token.ThrowIfCancellationRequested();
        if (Viewport != viewport) return;
        RenderedScale = scale; _imageViewport = viewport; _data = data; _bitmap = bitmap; InvalidateVisual();
    }
    public void Select(int time, int wave) { _time = time; _wave = wave; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_data == null || _bitmap == null) { dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize)); Text(dc, "Open a DAD file to display the intensity map", 25, 45); return; }
        var d = _data; var r = PlotArea;
        Axes(dc, d.TimeStamps[0], d.TimeStamps[^1], d.Wavelengths[0], d.Wavelengths[^1], "Time (min)", "Wavelength (nm)");
        if (_imageViewport == Viewport) dc.DrawImage(_bitmap, r);
        int t = Math.Clamp(_time, 0, d.NSpect - 1), w = Math.Clamp(_wave, 0, d.NWaves - 1);
        double x = r.Left + XFraction(d.TimeStamps[t], d.TimeStamps[0], d.TimeStamps[^1]) * r.Width;
        double y = r.Bottom - YFraction(d.Wavelengths[w], d.Wavelengths[0], d.Wavelengths[^1]) * r.Height;
        dc.PushClip(new RectangleGeometry(r));
        foreach (var pen in new[] { new Pen(Brushes.Black, 3), new Pen(Brushes.White, 1) })
        {
            dc.DrawLine(pen, new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawLine(pen, new Point(r.Left, y), new Point(r.Right, y));
        }
        dc.Pop(); DrawInteraction(dc);
    }
    protected override void PointChosen(Point point)
    {
        if (_data == null) return;
        var (t, w) = Indices(point);
        DataPointSelected?.Invoke(this, new(t, w));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_data == null || !PlotArea.Contains(e.GetPosition(this))) { ToolTip = null; return; }
        var (t, w) = Indices(e.GetPosition(this));
        ToolTip = $"Time: {_data.TimeStamps[t]:G6} min\nWavelength: {_data.Wavelengths[w]:G6} nm\nIntensity: {_data.GetIntensity(t, w):G6}";
    }
    private (int, int) Indices(Point p)
    {
        var d = _data!; var r = PlotArea;
        return (d.FindTime(XValue(Math.Clamp((p.X - r.Left) / r.Width, 0, 1), d.TimeStamps[0], d.TimeStamps[^1])),
            d.FindWavelength(YValue(Math.Clamp((r.Bottom - p.Y) / r.Height, 0, 1), d.Wavelengths[0], d.Wavelengths[^1])));
    }
}
