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
        var viewModel = new MainViewModel(new LoadDataService(new DADDataRepository()));
        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
        if (e.Args.Length == 1) await viewModel.LoadAsync(e.Args[0]);
    }
}
