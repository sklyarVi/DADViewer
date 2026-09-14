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
    public bool HasImage => _bitmap != null;
    public void Clear() { _data = null; _bitmap = null; InvalidateVisual(); }
    public event EventHandler<DataPointEventArgs>? DataPointSelected;
    public async Task RenderAsync(DADData data, ColorScale scale, CancellationToken token)
    {
        // Bounded raster samples physical coordinates, so irregular axes remain aligned with picking.
        var bitmap = await Task.Run(() =>
        {
            const int width = 1000, height = 500;
            var pixels = new byte[width * height * 4];
            var times = new int[width]; var waves = new int[height];
            for (int x = 0; x < width; x++) times[x] = data.FindTime(data.TimeStamps[0] + (data.TimeStamps[^1] - data.TimeStamps[0]) * x / (width - 1));
            for (int y = 0; y < height; y++) waves[y] = data.FindWavelength(data.Wavelengths[^1] - (data.Wavelengths[^1] - data.Wavelengths[0]) * y / (height - 1));
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
        _data = data; _bitmap = bitmap; InvalidateVisual();
    }
    public void Select(int time, int wave) { _time = time; _wave = wave; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_data == null || _bitmap == null) { dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize)); Text(dc, "Open a DAD file to display the intensity map", 25, 45); return; }
        var d = _data; var r = PlotArea;
        Axes(dc, d.TimeStamps[0], d.TimeStamps[^1], d.Wavelengths[0], d.Wavelengths[^1], "Time (file units)", "Wavelength (nm)");
        dc.DrawImage(_bitmap, r);
        int t = Math.Clamp(_time, 0, d.NSpect - 1), w = Math.Clamp(_wave, 0, d.NWaves - 1);
        double x = r.Left + Fraction(d.TimeStamps[t], d.TimeStamps[0], d.TimeStamps[^1]) * r.Width;
        double y = r.Bottom - Fraction(d.Wavelengths[w], d.Wavelengths[0], d.Wavelengths[^1]) * r.Height;
        dc.PushClip(new RectangleGeometry(r));
        foreach (var pen in new[] { new Pen(Brushes.Black, 3), new Pen(Brushes.White, 1) })
        {
            dc.DrawLine(pen, new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawLine(pen, new Point(r.Left, y), new Point(r.Right, y));
        }
        dc.Pop();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_data == null || !PlotArea.Contains(e.GetPosition(this))) return;
        var (t, w) = Indices(e.GetPosition(this));
        DataPointSelected?.Invoke(this, new(t, w));
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_data == null || !PlotArea.Contains(e.GetPosition(this))) { ToolTip = null; return; }
        var (t, w) = Indices(e.GetPosition(this));
        ToolTip = $"Time: {_data.TimeStamps[t]:G6}\nWavelength: {_data.Wavelengths[w]:G6} nm\nIntensity: {_data.GetIntensity(t, w):G6}";
    }
    private (int, int) Indices(Point p)
    {
        var d = _data!; var r = PlotArea;
        return (d.FindTime(d.TimeStamps[0] + Math.Clamp((p.X - r.Left) / r.Width, 0, 1) * (d.TimeStamps[^1] - d.TimeStamps[0])),
            d.FindWavelength(d.Wavelengths[0] + Math.Clamp((r.Bottom - p.Y) / r.Height, 0, 1) * (d.Wavelengths[^1] - d.Wavelengths[0])));
    }
}
