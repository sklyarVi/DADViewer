using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DADViewer.Application;
using DADViewer.Infrastructure;
using DADViewer.Presentation;
namespace DADViewer.Tests;

[Collection("WPF")]
public sealed class AnalysisWindowTests
{
    private sealed class Loader(DADData reference) : ILoadDataService
    {
        public Task<DADData> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
            => path == "bad" ? Task.FromException<DADData>(new IOException("Invalid reference")) : Task.FromResult(reference);
    }
    [Fact]
    public Task AnalysisParametersReferenceExportAndCompactLayoutWork() => RenderingTests.OnSta(async () =>
    {
        double[] times = Enumerable.Range(0, 501).Select(i => i * 0.02).ToArray();
        var values = new double[times.Length, 3]; var second = new double[times.Length, 3];
        for (int t = 0; t < times.Length; t++) for (int w = 0; w < 3; w++)
        {
            values[t, w] = 2 + (w + 1) * (10 * Math.Exp(-Math.Pow((times[t] - 3) / 0.4, 2)) + 6 * Math.Exp(-Math.Pow((times[t] - 7) / 0.6, 2)));
            second[t, w] = values[t, w] * 0.8;
        }
        var data = new DADData(times, [200, 210, 220], values);
        var reference = new DADData(times, [200, 210, 220], second);
        var window = new AnalysisWindow(new Loader(reference), data, "primary.dad", 150, 1, new FileExportService()) { ShowInTaskbar = false };
        string directory = Path.Combine(Path.GetTempPath(), "dad-analysis-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try
        {
            window.Show(); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await window.AnalysisTask;
            Assert.Equal(2, window.Result!.Peaks.Peaks.Count); Assert.NotNull(window.Icon);
            using (var icon = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/DADViewer.Presentation;component/Assets/DADViewer.ico")).Stream)
            {
                var decoder = BitmapDecoder.Create(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                Assert.Equal(new[] { 16, 20, 24, 32, 48, 64, 128, 256 }, decoder.Frames.Select(frame => frame.PixelWidth).Order().ToArray());
            }
            await window.LoadReferenceAsync("reference.dad");
            Assert.Equal(times.Length, window.Result!.Comparison!.Points.Count); Assert.True(window.Result.Comparison.RootMeanSquareError > 0);
            var valid = window.Result; await window.LoadReferenceAsync("bad"); Assert.Same(valid, window.Result);
            string peaks = Path.Combine(directory, "peaks.csv"), comparison = Path.Combine(directory, "comparison.csv");
            await window.ExportAsync(peaks, false); await window.ExportAsync(comparison, true);
            Assert.Equal(3, File.ReadAllLines(peaks).Length); Assert.Equal(times.Length + 1, File.ReadAllLines(comparison).Length);
            ((DataGrid)window.FindName("Peaks")).SelectedIndex = 0;
            await Snapshot(window, "analysis-normal.png", 1180, 840);
            await Snapshot(window, "analysis-compact.png", 920, 700);
            ((TextBox)window.FindName("Prominence")).Text = "1000";
            Assert.Null(window.Result); Assert.False(((Button)window.FindName("ExportPeaks")).IsEnabled);
            await window.AnalyzeAsync(); Assert.Empty(window.Result!.Peaks.Peaks);
            ((ComboBox)window.FindName("Mode")).SelectedIndex = 1;
            await window.AnalyzeAsync(); Assert.True(window.Result!.Request.Spectrum); Assert.Equal(3, window.Result.X.Count);
            ((TextBox)window.FindName("Coordinate")).Text = "-1";
            await window.AnalyzeAsync(); Assert.Null(window.Result); Assert.Contains("within", ((TextBlock)window.FindName("Status")).Text);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    });
    private sealed class WaitingLoader : ILoadDataService
    {
        public CancellationToken Token { get; private set; }
        public async Task<DADData> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        {
            Token = cancellationToken;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable");
        }
    }
    [Fact]
    public Task ClosingWindowCancelsPendingReferenceLoad() => RenderingTests.OnSta(async () =>
    {
        var data = new DADData([0, 1, 2], [200], new double[,] { { 0 }, { 2 }, { 0 } });
        var loader = new WaitingLoader();
        var window = new AnalysisWindow(loader, data, "primary.dad", 0, 0) { ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await window.AnalysisTask;
            var pending = window.LoadReferenceAsync("waiting.dad");
            window.Close(); await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(loader.Token.IsCancellationRequested);
        }
        finally { if (window.IsVisible) window.Close(); }
    });
    private static async Task Snapshot(Window window, string name, int width, int height)
    {
        window.Width = width; window.Height = height;
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var content = (FrameworkElement)window.Content; content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32); var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, content.ActualWidth, content.ActualHeight)); bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        Assert.True(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4 + 2] > 150 && pixels[i * 4 + 1] > 40 && pixels[i * 4 + 1] < 200 && pixels[i * 4] < 100) > 30, "Reference curve should be visible.");
        string? directory = Environment.GetEnvironmentVariable("DADVIEWER_SNAPSHOT_DIR");
        if (directory == null) return;
        Directory.CreateDirectory(directory); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }
}
