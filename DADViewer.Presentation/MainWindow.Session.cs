using System.IO;
using System.Windows;
using System.Windows.Controls;
using DADViewer.Application;
using DADViewer.Presentation.Rendering;
using DADViewer.Presentation.Views;
namespace DADViewer.Presentation;

public partial class MainWindow
{
    private readonly IUserStateStore? _stateStore;
    private readonly IDiagnostics? _diagnostics;
    private UserState _state = new();
    private long _loadedLength, _loadedWriteTicks;
    public async Task RestoreSessionAsync(string? commandLinePath = null)
    {
        _state = _stateStore?.Load() ?? new();
        Width = Math.Min(_state.Width, Math.Max(MinWidth, SystemParameters.WorkArea.Width));
        Height = Math.Min(_state.Height, Math.Max(MinHeight, SystemParameters.WorkArea.Height));
        if (_state.Maximized) WindowState = WindowState.Maximized;
        ReopenLast.IsChecked = _state.ReopenLastFile;
        if (Enum.TryParse<ColorScheme>(_state.ColorScheme, out var scheme) && Enum.IsDefined(scheme)) _viewModel.Scheme = scheme;
        _viewModel.ColorSteps = _state.ColorSteps;
        string? path = commandLinePath ?? (_state.ReopenLastFile ? _state.LastFile : null);
        if (!string.IsNullOrWhiteSpace(path)) await LoadFileAsync(path);
        if (_stateStore?.Warning is { } warning) _viewModel.Status += " " + warning;
    }
    public async Task LoadFileAsync(string path)
    {
        if (_viewModel.IsBusy) return;
        RememberFile();
        var before = _viewModel.Data;
        await _viewModel.LoadAsync(path);
        if (_closed || ReferenceEquals(before, _viewModel.Data)) return;
        try
        {
            var info = new FileInfo(_viewModel.FilePath);
            _loadedLength = info.Length; _loadedWriteTicks = info.LastWriteTimeUtc.Ticks;
            var saved = _state.Files.FirstOrDefault(f => string.Equals(f.Path, info.FullName, StringComparison.OrdinalIgnoreCase) && f.Length == info.Length && f.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks && f.SourceFingerprint == _viewModel.Data!.Metadata.Fingerprint);
            if (saved != null)
            {
                _viewModel.Select(saved.TimeIndex, saved.WavelengthIndex);
                try { _ = new ColorScale(saved.ColorMinimum, saved.ColorMaximum, _viewModel.ColorSteps, _viewModel.Scheme); _viewModel.ColorMinimum = saved.ColorMinimum; _viewModel.ColorMaximum = saved.ColorMaximum; }
                catch (ArgumentException) { _viewModel.ResetColorRange(); }
                RestoreViewport(MapView, saved.MapViewport); RestoreViewport(Chromatogram, saved.ChromatogramViewport); RestoreViewport(Spectrum, saved.SpectrumViewport);
                _viewModel.Show3D = saved.Show3D;
            }
            StartRendering();
            _state.LastFile = _viewModel.FilePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { _diagnostics?.Record("Restore file state", ex); }
    }
    private static void RestoreViewport(PlotSurface plot, double[]? values)
    { if (values is { Length: 4 } && values.All(double.IsFinite) && values[2] > 0 && values[3] > 0) plot.SetViewport(new Rect(values[0], values[1], values[2], values[3])); }
    private void RememberFile()
    {
        if (_viewModel.Data == null || string.IsNullOrEmpty(_viewModel.FilePath)) return;
        try
        {
            var info = new FileInfo(_viewModel.FilePath);
            if (!info.Exists) return;
            var scale = _appliedScale ?? new ColorScale(_viewModel.Data.MinIntensity, _viewModel.Data.MaxIntensity, _viewModel.ColorSteps, _viewModel.Scheme);
            static double[] Values(PlotSurface plot) => [plot.Viewport.X, plot.Viewport.Y, plot.Viewport.Width, plot.Viewport.Height];
            _state.Files.RemoveAll(f => string.Equals(f.Path, info.FullName, StringComparison.OrdinalIgnoreCase));
            _state.Files.Insert(0, new FileState { Path = info.FullName, Length = _loadedLength, LastWriteUtcTicks = _loadedWriteTicks,
                SourceFingerprint = _viewModel.Data.Metadata.Fingerprint, TimeIndex = _viewModel.TimeIndex, WavelengthIndex = _viewModel.WavelengthIndex, ColorMinimum = scale.Minimum, ColorMaximum = scale.Maximum,
                Show3D = _viewModel.Show3D, MapViewport = Values(MapView), ChromatogramViewport = Values(Chromatogram), SpectrumViewport = Values(Spectrum) });
            _state.Files = _state.Files.Take(10).ToList();
            _state.LastFile = info.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { _diagnostics?.Record("Remember file", ex); }
    }
    private void SaveSession()
    {
        if (_stateStore == null) return;
        RememberFile();
        _state.Width = WindowState == WindowState.Normal ? Width : RestoreBounds.Width;
        _state.Height = WindowState == WindowState.Normal ? Height : RestoreBounds.Height;
        _state.Maximized = WindowState == WindowState.Maximized;
        _state.ReopenLastFile = ReopenLast.IsChecked == true;
        _state.ColorScheme = _viewModel.Scheme.ToString(); _state.ColorSteps = _viewModel.ColorSteps;
        try { _stateStore.Save(_state); }
        catch (Exception ex) { _diagnostics?.Record("Save settings", ex); }
    }
    private void Recent_Click(object sender, RoutedEventArgs e)
    {
        RememberFile();
        var menu = new ContextMenu();
        foreach (var file in _state.Files)
        {
            var item = new MenuItem { Header = file.Path.Replace("_", "__"), IsEnabled = !_viewModel.IsBusy };
            item.Click += async (_, _) => await LoadFileAsync(file.Path); menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No recent files", IsEnabled = false });
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
}
