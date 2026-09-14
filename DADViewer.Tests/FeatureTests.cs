using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DADViewer.Application;
using DADViewer.Infrastructure;
using DADViewer.Presentation;
using DADViewer.Presentation.Rendering;
using DADViewer.Presentation.ViewModels;
using DADViewer.Presentation.Views;
namespace DADViewer.Tests;

[Collection("WPF")]
public sealed class FeatureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dad-features-" + Guid.NewGuid().ToString("N"));
    public FeatureTests() => Directory.CreateDirectory(_directory);
    [Fact]
    public void CsvPreservesExactValuesAndEscapesSourceUnderCommaDecimalCulture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var data = new DADData([0.125, 1.75], [200.5, 210.5], new double[,] { { -1.23456789012345, 2 }, { 3, 4 } });
            var settings = new ExportSettings("sample,\"quoted\".dad", 0, 1, -2, 4, 256, "Viridis");
            using var writer = new StringWriter(); CsvExport.Write(writer, data, SliceKind.Spectrum, settings);
            var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("0.125,200.5,-1.23456789012345,\"sample,\"\"quoted\"\".dad\"", lines[1]);
            using var chromatogram = new StringWriter(); CsvExport.Write(chromatogram, data, SliceKind.Chromatogram, settings);
            Assert.Contains("1.75,210.5,4,", chromatogram.ToString());
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Fact]
    public async Task FailedExportPreservesExistingDestinationAndCleansTemporaryFile()
    {
        string path = Path.Combine(_directory, "out.csv"); File.WriteAllText(path, "original");
        var service = new FileExportService();
        await Assert.ThrowsAsync<IOException>(() => service.SaveAsync(path, stream => { stream.WriteByte(1); throw new IOException("simulated full disk"); }));
        Assert.Equal("original", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(_directory));
        await service.SaveAsync(path, stream => stream.Write(Encoding.UTF8.GetBytes("replacement")));
        Assert.Equal("replacement", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(_directory));
    }
    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Version\":999}")]
    public void BadSettingsFallBackWithoutPreventingStartup(string json)
    {
        string path = Path.Combine(_directory, "settings.json"); File.WriteAllText(path, json);
        var store = new JsonUserStateStore(path); Assert.Equal(1280, store.Load().Width); Assert.NotNull(store.Warning);
    }
    [Fact]
    public void SettingsRoundTripAndSanitizeExternalValues()
    {
        string path = Path.Combine(_directory, "settings.json"); var store = new JsonUserStateStore(path);
        store.Save(new UserState { Width = 1400, LastFile = "sample.dad", Files = [new FileState { Path = "sample.dad", TimeIndex = 4 }] });
        Assert.Equal(4, store.Load().Files[0].TimeIndex);
        File.WriteAllText(path, "{\"Width\":-100,\"ColorSteps\":999,\"Files\":null}");
        var loaded = store.Load(); Assert.Equal(950, loaded.Width); Assert.Equal(256, loaded.ColorSteps); Assert.Empty(loaded.Files);
    }
    [Fact]
    public void ViridisMatchesOriginalLookupSamples()
    {
        Assert.Equal(Color.FromRgb(68, 1, 84), ColorMap.Get(0, 256, ColorScheme.Viridis));
        Assert.Equal(Color.FromRgb(33, 145, 140), ColorMap.Get(128 / 255d, 256, ColorScheme.Viridis));
        Assert.Equal(Color.FromRgb(253, 231, 37), ColorMap.Get(1, 256, ColorScheme.Viridis));
        // Two pairs coincide after conversion from reference floats to 8-bit RGB.
        Assert.Equal(254, Enumerable.Range(0, 256).Select(i => ColorMap.Get(i / 255d, 256, ColorScheme.Viridis)).Distinct().Count());
    }
    [Fact]
    public Task ZoomResamplesNarrowPeakFromSourceAndRejectsStaleImage() => RenderingTests.OnSta(async () =>
    {
        var values = new double[2001, 2]; values[999, 0] = values[999, 1] = 100;
        var data = new DADData(Enumerable.Range(0, 2001).Select(i => (double)i).ToArray(), [200, 201], values);
        var map = new DAD2DView(); var scale = new ColorScale(0, 100, 2, ColorScheme.Grayscale);
        map.Measure(new Size(600, 300)); map.Arrange(new Rect(0, 0, 600, 300));
        await map.RenderAsync(data, scale, CancellationToken.None);
        int BrightPixels()
        {
            map.UpdateLayout(); var image = new RenderTargetBitmap(600, 300, 96, 96, PixelFormats.Pbgra32); image.Render(map);
            var pixels = new byte[480 * 200 * 4]; image.CopyPixels(new Int32Rect(82, 35, 480, 200), pixels, 1920, 0);
            return Enumerable.Range(0, 96000).Count(i => pixels[i * 4] > 240);
        }
        Assert.InRange(BrightPixels(), 0, 500);
        map.SetViewport(new Rect(0.498, 0, 0.003, 1)); Assert.False(map.IsCurrentImage);
        await map.RenderAsync(data, scale, CancellationToken.None);
        Assert.True(map.IsCurrentImage); Assert.True(BrightPixels() > 10000);
        map.SetViewport(new Rect(-1, 2, 0.5, 0.5)); Assert.Equal(new Rect(0, 0.5, 0.5, 0.5), map.Viewport);
        map.Clear(); Assert.Equal(new Rect(0, 0, 1, 1), map.Viewport); Assert.False(map.HasImage);
    });
    [Fact]
    public Task SessionAndExportsUseActualSelectionAndVisibleScale() => RenderingTests.OnSta(async () =>
    {
        string sample = Path.Combine(AppContext.BaseDirectory, "SampleTestData", "TestData1.DAD");
        var store = new JsonUserStateStore(Path.Combine(_directory, "settings.json"));
        MainWindow Create(MainViewModel vm) => new(vm, new FileExportService(), store)
            { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
        using var vm = new MainViewModel(new LoadDataService(new DADDataRepository()));
        var first = Create(vm); first.Show();
        try
        {
            await first.RestoreSessionAsync(sample); await first.RenderingTask;
            vm.Select(1530, 43); vm.Scheme = ColorScheme.Jet; vm.ColorSteps = 64;
            var map = (DAD2DView)first.FindName("MapView"); map.SetViewport(new Rect(0.2, 0.1, 0.3, 0.6)); await first.RenderingTask;
            string csv = Path.Combine(_directory, "slice.csv"), png = Path.Combine(_directory, "map.png");
            await first.ExportAsync(csv, 1); await first.ExportAsync(png, 2);
            Assert.Equal(vm.Data!.NWaves + 1, File.ReadAllLines(csv).Length);
            using var file = File.OpenRead(png); var decoder = new PngBitmapDecoder(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert.True(decoder.Frames[0].PixelWidth >= 1600);
            string caption = (string)((BitmapMetadata)decoder.Frames[0].Metadata).GetQuery("/tEXt/{str=Description}");
            Assert.Contains("TestData1.DAD", caption); Assert.Contains("Jet, 64 colors", caption); Assert.Contains("Visible range: Time:", caption); Assert.Contains("nm", caption);
            await RenderingTests.Snapshot(first, "analysis-tools.png", 1280, 880);
            await RenderingTests.Snapshot(first, "analysis-tools-compact.png", 950, 680);
            if (Environment.GetEnvironmentVariable("DADVIEWER_SNAPSHOT_DIR") is { } dir) File.Copy(png, Path.Combine(dir, "export-map.png"), true);
        }
        finally { first.Close(); }
        using var restoredVm = new MainViewModel(new LoadDataService(new DADDataRepository()));
        var second = Create(restoredVm); second.Show();
        try
        {
            await second.RestoreSessionAsync(); await second.RenderingTask;
            Assert.Equal(1530, restoredVm.TimeIndex); Assert.Equal(43, restoredVm.WavelengthIndex); Assert.Equal(ColorScheme.Jet, restoredVm.Scheme);
            Assert.Equal(new Rect(0.2, 0.1, 0.3, 0.6), ((DAD2DView)second.FindName("MapView")).Viewport);
            await second.LoadFileAsync(Path.Combine(_directory, "missing.dad")); Assert.Equal(1530, restoredVm.TimeIndex);
        }
        finally { second.Close(); }
    });
    [Fact]
    public void DeepZoomTicksRemainDistinct()
    {
        Assert.NotEqual(PlotSurface.FormatTick(32.12345, 0.0001), PlotSurface.FormatTick(32.123475, 0.0001));
    }
    [Fact]
    public Task ChangedSourceDoesNotRestoreOldSelection() => RenderingTests.OnSta(async () =>
    {
        string sample = Path.Combine(_directory, "changed.dad");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SampleTestData", "TestData1.DAD"), sample);
        var store = new JsonUserStateStore(Path.Combine(_directory, "settings.json"));
        using var vm = new MainViewModel(new LoadDataService(new DADDataRepository()));
        var window = new MainWindow(vm, stateStore: store) { ShowActivated = false, ShowInTaskbar = false, Left = -32000, Top = -32000 };
        window.Show();
        try
        {
            await window.LoadFileAsync(sample); await window.RenderingTask; vm.Select(100, 20);
            File.SetLastWriteTimeUtc(sample, File.GetLastWriteTimeUtc(sample).AddMinutes(1));
            await window.LoadFileAsync(sample); await window.RenderingTask;
            Assert.Equal(0, vm.TimeIndex); Assert.Equal(0, vm.WavelengthIndex);
        }
        finally { window.Close(); }
    });
    public void Dispose() => Directory.Delete(_directory, true);
}
