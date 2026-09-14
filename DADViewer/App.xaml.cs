using System.IO;
using System.Windows;
using DADViewer.Application;
using DADViewer.Infrastructure;
using DADViewer.Presentation;
using DADViewer.Presentation.ViewModels;
namespace DADViewer;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string directory = Environment.GetEnvironmentVariable("DADVIEWER_DATA_DIRECTORY") is { Length: > 0 } custom
            ? Path.GetFullPath(custom) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DADViewer");
        var diagnostics = new FileDiagnostics(directory);
        DispatcherUnhandledException += (_, args) =>
        {
            diagnostics.Record("Unhandled UI exception", args.Exception);
            MessageBox.Show($"An unexpected error occurred. DAD Viewer will close.\nDiagnostic log: {directory}", "DAD Viewer", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true; Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { if (args.ExceptionObject is Exception ex) diagnostics.Record("Unhandled exception", ex); };
        var viewModel = new MainViewModel(new LoadDataService(new DADDataRepository()), diagnostics);
        var window = new MainWindow(viewModel, new FileExportService(), new JsonUserStateStore(Path.Combine(directory, "settings.json"), diagnostics), diagnostics);
        MainWindow = window;
        window.Show();
        await window.RestoreSessionAsync(e.Args.Length == 1 ? e.Args[0] : null);
    }
}
