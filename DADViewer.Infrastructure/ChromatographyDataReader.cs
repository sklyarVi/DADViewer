using DADViewer.Application;
using DADViewer.Domain;
using DADViewer.Infrastructure.Import;
namespace DADViewer.Infrastructure;

public sealed class ChromatographyDataReader : IDADDataReader
{
    public DADData LoadFromFile(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested(); path = Path.GetFullPath(path);
        if (Directory.Exists(path))
        {
            var candidates = Path.GetExtension(path).Equals(".raw", StringComparison.OrdinalIgnoreCase)
                ? WatersPdaReader.FindPdaFunctions(path)
                : Directory.EnumerateFiles(path).Where(p => Path.GetExtension(p).Equals(".uv", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (candidates.Count != 1) throw new FormatException(candidates.Count == 0 ? "No supported UV/PDA spectrum found in this folder." : "Multiple spectra found. Open the desired file: " + string.Join(", ", candidates.Select(Path.GetFileName)));
            path = candidates[0];
        }
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".uv" => new AgilentUvReader().Read(path, cancellationToken, progress),
                ".dat" => new WatersPdaReader().Read(path, cancellationToken, progress),
                ".txt" => new SpectralTextReader().Read(path, false, cancellationToken, progress),
                ".arw" => new SpectralTextReader().Read(path, true, cancellationToken, progress),
                ".dad" => ReadAssignment(path, cancellationToken, progress),
                _ => throw new FormatException("Unsupported format. Open assignment DAD, Agilent UV, Waters PDA DAT/ARW, or Chromeleon spectral TXT.")
            };
        }
        catch (OverflowException ex) { throw new FormatException("File dimensions or numeric fields exceed the supported range.", ex); }
    }
    private static DADData ReadAssignment(string path, CancellationToken token, IProgress<double>? progress)
    {
        try { return new DADDataRepository().LoadFromFile(path, token, progress); }
        catch (FormatException ex) { throw new FormatException("Invalid or unsupported DAD dialect. Native Chromeleon binary DAD is not supported; export the UV spectral field as ASCII TXT. " + ex.Message, ex); }
    }
}
