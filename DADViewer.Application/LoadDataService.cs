using DADViewer.Domain;
namespace DADViewer.Application;

public interface IDADDataReader
{
    DADData LoadFromFile(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null);
}

public interface ILoadDataService
{
    Task<DADData> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null);
}

public sealed class LoadDataService(IDADDataReader reader) : ILoadDataService
{
    public Task<DADData> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        => Task.Run(() => reader.LoadFromFile(path, cancellationToken, progress), cancellationToken);
}
