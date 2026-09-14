using System.IO;
namespace DADViewer.Application;

public static class DataSourceName
{
    public static string Short(string path)
    {
        string file = Path.GetFileName(path), parent = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        return parent.EndsWith(".raw", StringComparison.OrdinalIgnoreCase) || parent.EndsWith(".D", StringComparison.OrdinalIgnoreCase) ? parent + "/" + file : file;
    }
}
