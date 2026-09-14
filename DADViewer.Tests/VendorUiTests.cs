using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DADViewer.Application;
using DADViewer.Infrastructure;
using DADViewer.Presentation;
using DADViewer.Presentation.ViewModels;
namespace DADViewer.Tests;

[Collection("WPF")]
public sealed class VendorUiTests
{
    [Fact]
    public Task FolderImportUsesCanonicalSourcePersistsStateAndExportsUnits() => RenderingTests.OnSta(async () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "dad-vendor-ui-" + Guid.NewGuid().ToString("N"));
        string source = Path.Combine(directory, "Run-A.D"); Directory.CreateDirectory(source);
        string file = Path.Combine(source, "DAD1.uv"); VendorImportTests.WriteUv(file);
        var loader = new LoadDataService(new ChromatographyDataReader()); var state = new JsonUserStateStore(Path.Combine(directory, "settings.json"));
        var vm = new MainViewModel(loader); var window = new MainWindow(vm, new FileExportService(), state, analysisLoader: loader) { ShowInTaskbar = false };
        try
        {
            window.Show(); await window.LoadFileAsync(source); await window.RenderingTask;
            Assert.Equal(file, vm.FilePath); Assert.Equal("Run-A.D/DAD1.uv", vm.FileName); Assert.Contains("mAU", vm.Selection); Assert.Contains("Agilent", vm.Summary);
            string csv = Path.Combine(directory, "out.csv"); await window.ExportAsync(csv, 0);
            Assert.Contains("intensity_unit,source_format", File.ReadAllText(csv)); Assert.Contains("Run-A.D/DAD1.uv", File.ReadAllText(csv)); Assert.Contains("\"mAU\"", File.ReadAllText(csv));
            await window.ExportAsync(Path.Combine(directory, "map.png"), 2);
            vm.Select(1, 1); await RenderingTests.Snapshot(window, "vendor-main.png", 1280, 880); await RenderingTests.Snapshot(window, "vendor-main-compact.png", 950, 680);
            window.Close(); Assert.Equal(file, state.Load().LastFile); Assert.NotEmpty(state.Load().Files[0].SourceFingerprint);
            var againVm = new MainViewModel(loader); var again = new MainWindow(againVm, new FileExportService(), state, analysisLoader: loader) { ShowInTaskbar = false };
            try { again.Show(); await again.RestoreSessionAsync(); Assert.Equal(1, againVm.TimeIndex); Assert.Equal(1, againVm.WavelengthIndex); }
            finally { again.Close(); }
            var analysis = new AnalysisWindow(loader, new ChromatographyDataReader().LoadFromFile(file), file, 1, 1, new FileExportService()) { ShowInTaskbar = false };
            try
            {
                analysis.Show(); await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle); await analysis.AnalysisTask;
                await analysis.LoadReferenceAsync(source); Assert.NotNull(analysis.Result!.Comparison); Assert.Equal(0, analysis.Result.Comparison.RootMeanSquareError);
                Assert.Contains("mAU", ((TextBlock)analysis.FindName("UnitsLabel")).Text);
            }
            finally { analysis.Close(); }
        }
        finally { if (window.IsVisible) window.Close(); Directory.Delete(directory, true); }
    });
}
