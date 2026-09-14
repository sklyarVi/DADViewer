using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DADViewer.Application;
using DADViewer.Presentation;
using DADViewer.Presentation.Rendering;
using DADViewer.Presentation.ViewModels;
using DADViewer.Presentation.Views;
namespace DADViewer.Tests;

[Collection("WPF")]
public sealed class RenderingTests
{
    [Theory]
    [InlineData(ColorScheme.Viridis)]
    [InlineData(ColorScheme.Jet)]
    [InlineData(ColorScheme.Grayscale)]
    public void ColorStepsAreDiscreteAndEndpointsClamp(ColorScheme scheme)
    {
        var colors = Enumerable.Range(0, 1001).Select(i => ColorMap.Get(i / 1000d, 2, scheme)).Distinct();
        Assert.Equal(2, colors.Count());
        Assert.Equal(ColorMap.Get(0, 32, scheme), ColorMap.Get(-1, 32, scheme));
        Assert.Equal(ColorMap.Get(1, 32, scheme), ColorMap.Get(2, 32, scheme));
    }
    [Fact]
    public void ColorRangeRejectsInvalidValuesAndHandlesConstantSignal()
    {
        Assert.Throws<ArgumentException>(() => new ColorScale(2, 1, 32, ColorScheme.Viridis));
        Assert.Throws<ArgumentException>(() => new ColorScale(double.NaN, 1, 32, ColorScheme.Viridis));
        var scale = new ColorScale(7, 7, 32, ColorScheme.Viridis);
        Assert.Equal(ColorMap.Get(0.5, 32, ColorScheme.Viridis), scale.Map(7));
    }
    [Fact]
    public void SurfaceMeshIsBoundedAndAllCoordinatesAreFinite()
    {
        var data = new DADDataRepository().LoadFromFile(Path.Combine(AppContext.BaseDirectory, "SampleTestData", "TestData1.DAD"));
        var mesh = DAD3DView.BuildMesh(data, new ColorScale(data.MinIntensity, data.MaxIntensity, 32, ColorScheme.Viridis));
        Assert.True(mesh.IsFrozen);
        Assert.InRange(mesh.Positions.Count, 1, DAD3DView.MaxSamplesPerAxis * DAD3DView.MaxSamplesPerAxis);
        Assert.Equal((128 - 1) * (106 - 1) * 6, mesh.TriangleIndices.Count);
        Assert.All(mesh.TriangleIndices, index => Assert.InRange(index, 0, mesh.Positions.Count - 1));
        Assert.All(mesh.Positions, p => Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)));
    }
    [Fact]
    public Task WindowRendersRealDataAndReloadsWithoutStaleSelection() => OnSta(async () =>
    {
        using var vm = new MainViewModel(new LoadDataService(new DADDataRepository()));
        var window = new MainWindow(vm) { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
        window.Show();
        try
        {
            await vm.LoadAsync(Path.Combine(AppContext.BaseDirectory, "SampleTestData", "TestData1.DAD"));
            await window.RenderingTask;
            var map = (DAD2DView)window.FindName("MapView");
            var spectrum = (SlicePlot)window.FindName("Spectrum");
            var chromatogram = (SlicePlot)window.FindName("Chromatogram");
            Assert.True(map.HasImage); Assert.Equal(106, spectrum.PointCount); Assert.Equal(4869, chromatogram.PointCount);
            vm.Select(1530, 43);
            await Snapshot(window, "intensity-map.png", 1280, 840);
            await Snapshot(window, "compact-layout.png", 950, 680);
            vm.Show3D = true; await window.RenderingTask;
            var surface = (DAD3DView)window.FindName("SurfaceView"); Assert.Equal(128 * 106, surface.VertexCount);
            FindTab((DependencyObject)window.Content)!.SelectedIndex = 1;
            await Snapshot(window, "surface-3d.png", 1280, 840);
            await vm.LoadAsync(Path.Combine(AppContext.BaseDirectory, "SampleTestData", "TestData2.DAD")); await window.RenderingTask;
            Assert.Equal(0, vm.TimeIndex); Assert.Equal(0, vm.WavelengthIndex); Assert.Equal(3294, chromatogram.PointCount);
            vm.Show3D = false; await window.RenderingTask; Assert.Equal(0, surface.VertexCount);
            Assert.DoesNotContain("Unable", ((TextBlock)window.FindName("RenderStatus")).Text);
        }
        finally { window.Close(); }
    });
    [Fact]
    public Task SinglePointRenderingAndCancelledRenderAreSafe() => OnSta(async () =>
    {
        var data = new DADData([0], [200], new double[,] { { -2 } });
        var map = new DAD2DView(); var scale = new ColorScale(-2, -2, 2, ColorScheme.Grayscale);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => map.RenderAsync(data, scale, cts.Token));
        Assert.False(map.HasImage);
        await map.RenderAsync(data, scale, CancellationToken.None); Assert.True(map.HasImage);
        map.Measure(new Size(600, 300)); map.Arrange(new Rect(0, 0, 600, 300)); map.UpdateLayout();
        var bitmap = new RenderTargetBitmap(600, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(map);
        var surface = new DAD3DView(); await surface.RenderAsync(data, scale, CancellationToken.None); Assert.Equal(0, surface.VertexCount);
    });
    [Fact]
    public Task SurfaceControlProducesColoredPixels() => OnSta(async () =>
    {
        var data = new DADDataRepository().LoadFromFile(Path.Combine(AppContext.BaseDirectory, "SampleTestData", "TestData1.DAD"));
        var view = new DAD3DView();
        var host = new Window { Content = view, Width = 600, Height = 400, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000 };
        try
        {
            host.Show(); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await view.RenderAsync(data, new ColorScale(data.MinIntensity, data.MaxIntensity, 32, ColorScheme.Viridis), CancellationToken.None);
            view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(600, 400, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
            var pixels = new byte[600 * 400 * 4]; bitmap.CopyPixels(pixels, 2400, 0);
            int colored = Enumerable.Range(0, 240000).Count(i => pixels[i * 4 + 3] > 200 && Math.Abs(pixels[i * 4] - pixels[i * 4 + 2]) > 25);
            Assert.True(colored > 1000, $"Surface control has only {colored} colored pixels.");
        }
        finally { host.Close(); }
    });

    private static TabControl? FindTab(DependencyObject parent)
    {
        if (parent is TabControl tab) return tab;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindTab(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
    internal static async Task Snapshot(MainWindow window, string name, int width, int height)
    {
        window.Width = width; window.Height = height;
        var content = (FrameworkElement)window.Content;
        content.UpdateLayout();
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        string? directory = Environment.GetEnvironmentVariable("DADVIEWER_SNAPSHOT_DIR");
        if (directory == null) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);

    }
    internal static async Task OnSta(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, e) => { e.Handled = true; completion.TrySetException(e.Exception); dispatcher.BeginInvokeShutdown(DispatcherPriority.Send); };
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            }));
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }
}
