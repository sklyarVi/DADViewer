using DADViewer.Application;
namespace DADViewer.Infrastructure;

public sealed class FileExportService : IFileExportService
{
    public Task SaveAsync(string path, Action<Stream> write) => Task.Run(() => AtomicFile.Write(path, write));
}
internal static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        path = Path.GetFullPath(path);
        string temp = Path.Combine(Path.GetDirectoryName(path)!, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(stream); stream.Flush(true); }
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
