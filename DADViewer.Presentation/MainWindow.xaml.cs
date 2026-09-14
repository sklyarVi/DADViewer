using DADViewer.Application;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using DADViewer.Presentation.Rendering;
using DADViewer.Presentation.ViewModels;
using Microsoft.Win32;
namespace DADViewer.Presentation;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private CancellationTokenSource? _rendering;
    private bool _closed;
    private DADViewer.Domain.DADData? _surfaceData;
    private ColorScale? _surfaceScale;
    public Task RenderingTask { get; private set; } = Task.CompletedTask;
    public MainWindow(MainViewModel viewModel, IFileExportService? exports = null, IUserStateStore? stateStore = null, IDiagnostics? diagnostics = null)
    {
        InitializeComponent();
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag);
        _exports = exports; _stateStore = stateStore; _diagnostics = diagnostics;
        _viewModel = viewModel; DataContext = viewModel;
        MapView.ViewportChanged += (_, _) => StartRendering();
        SchemeSelector.ItemsSource = Enum.GetValues<ColorScheme>();
        _viewModel.PropertyChanged += ViewModelChanged;
        MapView.DataPointSelected += (_, point) => _viewModel.Select(point.TimeIndex, point.WavelengthIndex);
        Chromatogram.IndexSelected += index => _viewModel.TimeIndex = index;
        Spectrum.IndexSelected += index => _viewModel.WavelengthIndex = index;
    }
    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "DAD files (*.dad)|*.dad|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await LoadFileAsync(dialog.FileName);
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel.IsBusy) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files) await LoadFileAsync(files[0]);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _viewModel.CancelLoading();
    private void Apply_Click(object sender, RoutedEventArgs e) => StartRendering();
    private void AutoRange_Click(object sender, RoutedEventArgs e) { _viewModel.ResetColorRange(); StartRendering(); }
    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_closed) return;
        if (e.PropertyName == nameof(MainViewModel.Data)) { _appliedScale = null; MapView.Clear(); SurfaceView.Clear(); _surfaceData = null; UpdateSlices(); StartRendering(); }
        else if (e.PropertyName == nameof(MainViewModel.Selection)) UpdateSlices();
        else if (e.PropertyName is nameof(MainViewModel.Scheme) or nameof(MainViewModel.ColorSteps) or nameof(MainViewModel.Show3D)) StartRendering();
    }
    private void UpdateSlices()
    {
        var d = _viewModel.Data; if (d == null) return;
        MapView.Select(_viewModel.TimeIndex, _viewModel.WavelengthIndex);
        Chromatogram.SetSeries(d.TimeStamps, d.GetChromatogram(_viewModel.WavelengthIndex), _viewModel.TimeIndex, "Time (min)", $"Chromatogram · {d.Wavelengths[_viewModel.WavelengthIndex]:G5} nm");
        Spectrum.SetSeries(d.Wavelengths, d.GetSpectrum(_viewModel.TimeIndex), _viewModel.WavelengthIndex, "Wavelength (nm)", $"Spectrum · time {d.TimeStamps[_viewModel.TimeIndex]:G5}");
    }
    private void StartRendering()
    {
        if (_closed || _viewModel.Data == null) return;
        _rendering?.Cancel();
        var cts = new CancellationTokenSource(); _rendering = cts;
        RenderingTask = RefreshAsync(cts);
    }
    private async Task RefreshAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(100, cts.Token); // Coalesce continuous color-slider updates.
            var data = _viewModel.Data!; var scale = _viewModel.CreateScale();
            RenderStatus.Text = "Rendering…";
            var map = MapView.RenderAsync(data, scale, cts.Token);
            Task surface;
            if (_viewModel.Show3D) surface = ReferenceEquals(_surfaceData, data) && _surfaceScale == scale ? Task.CompletedTask : SurfaceView.RenderAsync(data, scale, cts.Token);
            else { SurfaceView.Clear(); _surfaceData = null; surface = Task.CompletedTask; }
            await Task.WhenAll(map, surface);
            cts.Token.ThrowIfCancellationRequested();
            if (_viewModel.Show3D) { _surfaceData = data; _surfaceScale = scale; }
            var gradient = new LinearGradientBrush();
            for (int i = 0; i < scale.Steps; i++)
            {
                var color = ColorMap.Get(i / (scale.Steps - 1d), scale.Steps, scale.Scheme);
                gradient.GradientStops.Add(new GradientStop(color, Math.Max(0, (i - 0.5) / (scale.Steps - 1))));
                gradient.GradientStops.Add(new GradientStop(color, Math.Min(1, (i + 0.5) / (scale.Steps - 1))));
            }
            _appliedScale = scale; ColorLegend.Fill = gradient;
            LegendRange.Text = $"{scale.Minimum:G6} … {scale.Maximum:G6} ({scale.Steps} colors)";
            RenderStatus.Text = "Views ready. Click a point to inspect exact values.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _diagnostics?.Record("Render", ex); if (ReferenceEquals(_rendering, cts) && !_closed) RenderStatus.Text = $"Unable to render: {ex.Message}"; }
        finally { if (ReferenceEquals(_rendering, cts)) _rendering = null; cts.Dispose(); }
    }
    protected override void OnClosed(EventArgs e)
    {
        SaveSession(); _closed = true; _viewModel.PropertyChanged -= ViewModelChanged;
        _rendering?.Cancel(); _viewModel.Dispose(); base.OnClosed(e);
    }
}
