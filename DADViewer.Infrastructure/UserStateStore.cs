using System.Text.Json;
using DADViewer.Application;
namespace DADViewer.Infrastructure;

public sealed class JsonUserStateStore(string path, IDiagnostics? diagnostics = null) : IUserStateStore
{
    public string? Warning { get; private set; }
    public UserState Load()
    {
        Warning = null;
        try
        {
            if (!File.Exists(path)) return new();
            if (new FileInfo(path).Length > 1024 * 1024) throw new FormatException("Settings file is too large.");
            var state = JsonSerializer.Deserialize<UserState>(File.ReadAllText(path)) ?? throw new FormatException("Empty settings.");
            if (state.Version != 1) throw new FormatException("Unsupported settings version.");
            state.Width = double.IsFinite(state.Width) ? Math.Clamp(state.Width, 950, 7680) : 1280;
            state.Height = double.IsFinite(state.Height) ? Math.Clamp(state.Height, 680, 4320) : 880;
            state.ColorSteps = Math.Clamp(state.ColorSteps, 2, 256);
            state.LastFile ??= "";
            state.Files = (state.Files ?? []).Where(f => f != null && !string.IsNullOrWhiteSpace(f.Path)).DistinctBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException)
        { Warning = "Saved settings could not be read. Defaults are in use."; diagnostics?.Record("Read settings", ex); return new(); }
    }
    public void Save(UserState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        AtomicFile.Write(path, stream => JsonSerializer.Serialize(stream, state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
public sealed class FileDiagnostics(string directory) : IDiagnostics
{
    private readonly object _gate = new();
    public void Record(string operation, Exception exception)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                    File.Move(path, Path.Combine(directory, "diagnostics.previous.log"), true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} | {operation} | {exception}\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { /* Logging must not replace the original error. */ }
    }
}
