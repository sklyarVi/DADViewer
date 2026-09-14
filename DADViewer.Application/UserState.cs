namespace DADViewer.Application;

public interface IDiagnostics { void Record(string operation, Exception exception); }
public interface IUserStateStore
{
    UserState Load();
    void Save(UserState state);
    string? Warning { get; }
}
public sealed class UserState
{
    public int Version { get; set; } = 1;
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 880;
    public bool Maximized { get; set; }
    public bool ReopenLastFile { get; set; } = true;
    public string LastFile { get; set; } = "";
    public string ColorScheme { get; set; } = "Viridis";
    public int ColorSteps { get; set; } = 32;
    public List<FileState> Files { get; set; } = [];
}
public sealed class FileState
{
    public string Path { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public long Length { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public int TimeIndex { get; set; }
    public int WavelengthIndex { get; set; }
    public double ColorMinimum { get; set; }
    public double ColorMaximum { get; set; }
    public bool Show3D { get; set; }
    public double[] MapViewport { get; set; } = [0, 0, 1, 1];
    public double[] ChromatogramViewport { get; set; } = [0, 0, 1, 1];
    public double[] SpectrumViewport { get; set; } = [0, 0, 1, 1];
}
