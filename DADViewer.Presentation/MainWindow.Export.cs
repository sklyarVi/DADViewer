using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using DADViewer.Application;
using DADViewer.Presentation.Rendering;
using DADViewer.Presentation.Views;
using Microsoft.Win32;
namespace DADViewer.Presentation;

public partial class MainWindow
{
    private readonly IFileExportService? _exports;
    private ColorScale? _appliedScale;
    private void ResetZoom_Click(object sender, RoutedEventArgs e) { MapView.ResetZoom(); Chromatogram.ResetZoom(); Spectrum.ResetZoom(); }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_exports == null || _viewModel.Data == null) return;
        int choice = ExportChoice.SelectedIndex;
        bool csv = choice < 2;
        var dialog = new SaveFileDialog { Filter = csv ? "CSV (*.csv)|*.csv" : "PNG (*.png)|*.png", AddExtension = true,
            FileName = Path.GetFileNameWithoutExtension(_viewModel.FileName) + "-" + new[] { "chromatogram", "spectrum", "map", "chromatogram", "spectrum" }[choice], OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        ExportButton.IsEnabled = false;
        try { await ExportAsync(dialog.FileName, choice); if (!_closed) _viewModel.Status = $"Exported: {dialog.FileName}"; }
        catch (Exception ex) { if (!_closed) _viewModel.Status = $"Unable to export: {ex.Message}"; _diagnostics?.Record("Export", ex); }
        finally { ExportButton.IsEnabled = true; }
    }
    public async Task ExportAsync(string path, int choice)
    {
        if (_exports == null) throw new InvalidOperationException("Export service is unavailable.");
        if (choice is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(choice));
        // Wait for the latest render even when a preceding render was superseded.
        Task pending;
        do { pending = RenderingTask; await pending; } while (pending != RenderingTask);
        var data = _viewModel.Data ?? throw new InvalidOperationException("Open a DAD file first.");
        var scale = _appliedScale ?? throw new InvalidOperationException("Apply a valid color scale first.");
        var settings = new ExportSettings(_viewModel.FilePath, _viewModel.TimeIndex, _viewModel.WavelengthIndex, scale.Minimum, scale.Maximum, scale.Steps, scale.Scheme.ToString());
        if (choice < 2)
        {
            await _exports.SaveAsync(path, stream =>
            {
                using var writer = new StreamWriter(stream, new UTF8Encoding(true), 4096, leaveOpen: true);
                CsvExport.Write(writer, data, choice == 0 ? SliceKind.Chromatogram : SliceKind.Spectrum, settings);
            });
        }
        else
        {
            PlotSurface plot = choice == 2 ? MapView : choice == 3 ? Chromatogram : Spectrum;
            if (choice == 2 && (!MapView.IsCurrentImage || MapView.RenderedScale != scale)) throw new InvalidOperationException("The map has not finished rendering. Apply a valid scale and try again.");
            string caption = $"Source: {_viewModel.FileName}\n{_viewModel.Selection}\nIntensity: {data.Metadata.IntensityUnit}; format: {data.Metadata.Format}; color scale: {scale.Minimum:G6} … {scale.Maximum:G6}; {scale.Scheme}, {scale.Steps} colors\nVisible range: {plot.VisibleRangeDescription}";
            var bytes = PngExport.Capture(plot, caption, scale);
            await _exports.SaveAsync(path, stream => stream.Write(bytes));
        }
    }
}
