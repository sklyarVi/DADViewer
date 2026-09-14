using System.IO;
using DADViewer.Application;
using DADViewer.Domain;
using DADViewer.Presentation.Rendering;
namespace DADViewer.Presentation.ViewModels;

public sealed class MainViewModel(ILoadDataService loader, IDiagnostics? diagnostics = null) : ViewModelBase, IDisposable
{
    private CancellationTokenSource? _loading;
    private bool _disposed;
    private DADData? _data;
    private int _time, _wave;
    private bool _busy, _show3D;
    private string _status = "Open a DAD file to begin.", _fileName = "";
    private double _progress, _minimum, _maximum;
    private int _steps = 32;
    private ColorScheme _scheme = ColorScheme.Viridis;
    public DADData? Data => _data;
    public bool HasData => _data != null;
    public bool IsBusy { get => _busy; private set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanOpen)); } }
    public bool CanOpen => !IsBusy;
    public double Progress { get => _progress; private set { _progress = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
    public string FileName => _fileName; public string FilePath { get; private set; } = "";
    public int MaxTimeIndex => (_data?.NSpect ?? 1) - 1;
    public int MaxWavelengthIndex => (_data?.NWaves ?? 1) - 1;
    public int TimeIndex { get => _time; set => Select(value, _wave); }
    public int WavelengthIndex { get => _wave; set => Select(_time, value); }
    public string Selection => _data == null ? "" : $"Time: {_data.TimeStamps[_time]:G6} min  |  Wavelength: {_data.Wavelengths[_wave]:G6} nm  |  Intensity: {_data.GetIntensity(_time, _wave):G6}";
    public string Summary => _data == null ? "" : $"{_data.NSpect:N0} spectra × {_data.NWaves:N0} wavelengths  |  Signal: {_data.MinIntensity:G6} … {_data.MaxIntensity:G6}";
    public int ColorSteps { get => _steps; set { value = Math.Clamp(value, 2, 256); if (_steps == value) return; _steps = value; OnPropertyChanged(); } }
    public ColorScheme Scheme { get => _scheme; set { if (_scheme == value) return; _scheme = value; OnPropertyChanged(); } }
    public double ColorMinimum { get => _minimum; set { _minimum = value; OnPropertyChanged(); } }
    public double ColorMaximum { get => _maximum; set { _maximum = value; OnPropertyChanged(); } }
    public bool Show3D { get => _show3D; set { _show3D = value; OnPropertyChanged(); } }
    public ColorScale CreateScale() => new(_minimum, _maximum, _steps, _scheme);
    public void ResetColorRange()
    {
        if (_data == null) return;
        _minimum = _data.MinIntensity; _maximum = _data.MaxIntensity;
        OnPropertyChanged(nameof(ColorMinimum)); OnPropertyChanged(nameof(ColorMaximum));
    }
    public void Select(int time, int wave)
    {
        if (_data == null) return;
        time = Math.Clamp(time, 0, MaxTimeIndex); wave = Math.Clamp(wave, 0, MaxWavelengthIndex);
        if (time == _time && wave == _wave) return;
        _time = time; _wave = wave;
        OnPropertyChanged(nameof(TimeIndex)); OnPropertyChanged(nameof(WavelengthIndex)); OnPropertyChanged(nameof(Selection));
    }
    public async Task LoadAsync(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Ignore re-entrant opens; cancellation preserves the last complete dataset.
        if (IsBusy) return;
        var cts = new CancellationTokenSource(); _loading = cts;
        IsBusy = true; Progress = 0; Status = "Loading and validating data…";
        try
        {
            var progress = new Progress<double>(value => { if (ReferenceEquals(_loading, cts)) Progress = value; });
            var loaded = await loader.LoadAsync(path, cts.Token, progress);
            cts.Token.ThrowIfCancellationRequested();
            FilePath = Path.GetFullPath(path); _data = loaded; _fileName = Path.GetFileName(path); _time = 0; _wave = 0;
            _minimum = loaded.MinIntensity; _maximum = loaded.MaxIntensity;
            Status = "File loaded.";
            foreach (string name in new[] { nameof(Data), nameof(HasData), nameof(FileName), nameof(MaxTimeIndex), nameof(MaxWavelengthIndex), nameof(TimeIndex), nameof(WavelengthIndex), nameof(Selection), nameof(Summary), nameof(ColorMinimum), nameof(ColorMaximum) }) OnPropertyChanged(name);
        }
        catch (OperationCanceledException) { Status = "Loading cancelled. Previous data retained."; }
        catch (Exception ex) { diagnostics?.Record("Load DAD", ex); Status = $"Unable to load file: {ex.Message}"; }
        finally { if (ReferenceEquals(_loading, cts)) { _loading = null; IsBusy = false; } cts.Dispose(); }
    }
    public void CancelLoading() => _loading?.Cancel();
    public void Dispose() { _disposed = true; _loading?.Cancel(); }
}
