using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DADViewer.Application;
using DADViewer.Domain;
using Microsoft.Win32;
namespace DADViewer.Presentation;

public partial class AnalysisWindow : Window
{
    private readonly ILoadDataService _loader;
    private readonly IFileExportService? _exports;
    private readonly IDiagnostics? _diagnostics;
    private readonly DADData _data;
    private readonly string _source;
    private readonly double _initialTime, _initialWave;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private DADData? _reference;
    private string _referenceSource = "";
    private bool _ready, _busy, _closed;
    public AnalysisSnapshot? Result { get; private set; }
    public Task AnalysisTask { get; private set; } = Task.CompletedTask;
    public AnalysisWindow(ILoadDataService loader, DADData data, string source, int timeIndex, int wavelengthIndex, IFileExportService? exports = null, IDiagnostics? diagnostics = null)
    {
        _token = _lifetime.Token; _loader = loader; _data = data; _source = source; _exports = exports; _diagnostics = diagnostics;
        _initialTime = data.TimeStamps[timeIndex]; _initialWave = data.Wavelengths[wavelengthIndex];
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        SourceLabel.Text = $"Primary: {Path.GetFileName(source)} · snapshot of the open file";
        Coordinate.Text = _initialWave.ToString("R", CultureInfo.CurrentCulture);
        var initial = data.GetChromatogram(wavelengthIndex);
        Prominence.Text = ((initial.Max() - initial.Min()) * 0.03).ToString("G6", CultureInfo.CurrentCulture);
        Plot.IndexSelected += index => { if (Result is { } result) ShowCurve(result, index); };
        _ready = true;
        Loaded += (_, _) => AnalysisTask = AnalyzeAsync();
    }
    private void InvalidateResult()
    {
        if (!_ready || _busy) return;
        Result = null; Peaks.ItemsSource = null; Metrics.Text = "";
        ExportPeaks.IsEnabled = ExportComparison.IsEnabled = false;
        Plot.SetAnalysis(null, null, null, null);
        Status.Text = "Parameters changed. Click Analyze to update the graph and results.";
    }
    private void Input_Changed(object sender, TextChangedEventArgs e) => InvalidateResult();
    private void Parameter_Changed(object sender, RoutedEventArgs e) => InvalidateResult();
    private void Mode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        bool spectrum = Mode.SelectedIndex == 1;
        CoordinateLabel.Text = spectrum ? "Time (min)" : "Wavelength (nm)";
        DistanceLabel.Text = spectrum ? "Min. distance (nm)" : "Min. distance (min)";
        Coordinate.Text = (spectrum ? _initialTime : _initialWave).ToString("R", CultureInfo.CurrentCulture);
        Distance.Text = "0"; Prominence.Text = "0"; Plot.ResetZoom(); InvalidateResult();
    }
    private static double Number(TextBox box)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value) || !double.IsFinite(value))
            throw new ArgumentException("Enter finite numeric parameters using your system's decimal separator.");
        return value;
    }
    private AnalysisRequest Request() => new(_data, _source, Mode.SelectedIndex == 1, Number(Coordinate),
        new(Number(Prominence), Number(Distance), Baseline.SelectedIndex == 0 ? BaselineMode.EndpointLine : BaselineMode.Zero, Negative.IsChecked == true), _reference, _referenceSource);
    public Task AnalyzeAsync() => RunAsync(AnalyzeCoreAsync);
    private async Task AnalyzeCoreAsync()
    {
        Result = null; Peaks.ItemsSource = null; Metrics.Text = "";
        Plot.SetAnalysis(null, null, null, null);
        var request = Request(); Status.Text = "Calculating from full-resolution data…";
        var result = await Task.Run(() => SliceAnalysis.Analyze(request, _token), _token);
        _token.ThrowIfCancellationRequested();
        Result = result; Peaks.ItemsSource = result.Peaks.Peaks;
        ShowCurve(result, -1);
        UnitsLabel.Text = $"Position / width: {result.AxisUnit} · Height / prominence: file intensity units · Area: file intensity units × {result.AxisUnit}. Analysis uses the full slice, regardless of zoom.";
        ReferenceLabel.Text = $"Blue: primary at {result.Coordinate:G6} {result.FixedUnit}" + (result.ReferenceCoordinate is { } coordinate ? $" · Orange: {Path.GetFileName(_referenceSource)} at {coordinate:G6} {result.FixedUnit} (nearest measured slice)" : "") + " · Gray: baseline · Teal: peaks";
        Metrics.Text = result.Comparison is { } comparison
            ? comparison.Points.Count == 0 ? "No primary sample points in the shared axis range. Comparison metrics unavailable."
            : $"Comparison: {comparison.Points.Count:N0} primary points · MAE {comparison.MeanAbsoluteError:G6} · RMSE {comparison.RootMeanSquareError:G6} (file units). Reference interpolated within overlap; no shift or normalization."
            : _reference != null ? "Reference does not cover this slice coordinate. Choose a coordinate within both files to compare." : "Open a reference file to overlay and compare the same slice.";
        Status.Text = $"{result.Peaks.Peaks.Count} primary peaks shown" + (result.Peaks.DetectedCount > result.Peaks.Peaks.Count ? $" of {result.Peaks.DetectedCount}; strongest {request.Options.MaximumPeaks} retained" : "") + ". Select a row to highlight its peak.";
    }
    private void ShowCurve(AnalysisSnapshot result, int selected)
    {
        Plot.SetSeries(result.X, result.Y, selected, result.Request.Spectrum ? "Wavelength (nm)" : "Time (min)", $"{(result.Request.Spectrum ? "Spectrum" : "Chromatogram")} · {result.Coordinate:G6} {result.FixedUnit}");
        Plot.SetAnalysis(result.Peaks.Baseline, result.Peaks.Peaks.Select(p => p.Index).ToArray(), result.ReferenceX, result.ReferenceY);
    }
    public Task LoadReferenceAsync(string path) => RunAsync(async () =>
    {
        Status.Text = "Loading reference…";
        var data = await _loader.LoadAsync(path, _token);
        _token.ThrowIfCancellationRequested();
        _reference = data; _referenceSource = path; Plot.ResetZoom();
        await AnalyzeCoreAsync();
    });
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _closed) return;
        _busy = true; InputPanel.IsEnabled = false; ExportPeaks.IsEnabled = ExportComparison.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _diagnostics?.Record("Analyze / compare", ex); if (!_closed) Status.Text = ex.Message; }
        finally
        {
            _busy = false;
            if (!_closed)
            {
                InputPanel.IsEnabled = true;
                ExportPeaks.IsEnabled = _exports != null && Result != null;
                ExportComparison.IsEnabled = _exports != null && Result?.Comparison?.Points.Count > 0;
            }
        }
    }
    private void Analyze_Click(object sender, RoutedEventArgs e) => AnalysisTask = AnalyzeAsync();
    private async void Reference_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DAD files (*.dad)|*.dad", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) { AnalysisTask = LoadReferenceAsync(dialog.FileName); await AnalysisTask; }
    }
    private void RemoveReference_Click(object sender, RoutedEventArgs e)
    { _reference = null; _referenceSource = ""; Plot.ResetZoom(); AnalysisTask = AnalyzeAsync(); }
    private void Reset_Click(object sender, RoutedEventArgs e) => Plot.ResetZoom();
    private void Peak_Selected(object sender, SelectionChangedEventArgs e)
    { if (Result is { } result && Peaks.SelectedItem is PeakResult peak) ShowCurve(result, peak.Index); }
    private async void ExportPeaks_Click(object sender, RoutedEventArgs e) => await ChooseExportAsync(false);
    private async void ExportComparison_Click(object sender, RoutedEventArgs e) => await ChooseExportAsync(true);
    private async Task ChooseExportAsync(bool comparison)
    {
        var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", DefaultExt = ".csv", FileName = Path.GetFileNameWithoutExtension(_source) + (comparison ? "-comparison.csv" : "-peaks.csv") };
        if (dialog.ShowDialog(this) == true) await ExportAsync(dialog.FileName, comparison);
    }
    public Task ExportAsync(string path, bool comparison) => RunAsync(async () =>
    {
        var result = Result ?? throw new InvalidOperationException("Analyze the current parameters first.");
        if (_exports == null) throw new InvalidOperationException("Export service unavailable.");
        if (comparison && result.Comparison?.Points.Count is not > 0) throw new InvalidOperationException("No overlapping comparison points to export.");
        await _exports.SaveAsync(path, stream => AnalysisCsv.Write(stream, result, comparison));
        if (!_closed) Status.Text = $"Exported {Path.GetFileName(path)}.";
    });
    protected override void OnClosed(EventArgs e)
    { _closed = true; _lifetime.Cancel(); _lifetime.Dispose(); base.OnClosed(e); }
}
