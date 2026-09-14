using System.Text.RegularExpressions;
using DADViewer.Domain;
namespace DADViewer.Infrastructure.Import;

public sealed class WatersPdaReader
{
    private static string Companion(string directory, string name) => Directory.EnumerateFiles(directory).FirstOrDefault(p => Path.GetFileName(p).Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new FormatException($"Waters companion file is missing: {name}. Keep the original .raw directory together.");
    public static IReadOnlyList<string> FindPdaFunctions(string directory)
    {
        string info = Companion(directory, "_FUNCTNS.INF"); using var stream = ImportGuard.Open(info);
        if (stream.Length == 0 || stream.Length % 416 != 0 || stream.Length > 416 * 1000) throw new FormatException("Invalid Waters function metadata.");
        var result = new List<string>();
        for (int i = 0; i < stream.Length / 416; i++) { stream.Position = i * 416; if ((stream.ReadByte() & 31) == 12) result.Add(Companion(directory, $"_FUNC{i + 1:000}.DAT")); }
        return result;
    }
    public DADData Read(string path, CancellationToken token = default, IProgress<double>? progress = null)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var match = Regex.Match(Path.GetFileName(path), @"^_FUNC(\d{3})\.DAT$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out int function) || function == 0) throw new FormatException("Select a Waters _FUNCnnn.DAT file inside its .raw folder.");
        string inf = Companion(directory, "_FUNCTNS.INF"), idx = Companion(directory, $"_FUNC{function:000}.IDX");
        using var info = ImportGuard.Open(inf); using var index = ImportGuard.Open(idx); using var data = ImportGuard.Open(path);
        using var ir = new BinaryReader(index); using var dr = new BinaryReader(data);
        try
        {
            token.ThrowIfCancellationRequested();
            if (info.Length % 416 != 0 || info.Length < function * 416) throw new FormatException("Incomplete Waters function metadata.");
            info.Position = (function - 1) * 416;
            if ((info.ReadByte() & 31) != 12) throw new FormatException("Selected Waters function is not a confirmed PDA/DAD wavelength spectrum (type 12). MS and analog channels are not supported.");
            if (index.Length == 0 || index.Length % 22 != 0) throw new FormatException("Unsupported Waters index layout; expected 22-byte scan records.");
            int rows = checked((int)(index.Length / 22)); ImportGuard.Dimensions(rows, 1);
            var times = new double[rows]; var offsets = new uint[rows]; var counts = new int[rows]; long points = 0;
            for (int t = 0; t < rows; t++)
            {
                token.ThrowIfCancellationRequested(); index.Position = t * 22L;
                offsets[t] = ir.ReadUInt32(); counts[t] = (int)(ir.ReadUInt32() & 0x3FFFFF);
                index.Position += 4; times[t] = ir.ReadSingle(); points += counts[t];
                if (counts[t] == 0 || points > DADDataRepository.MaxDataPoints) throw new FormatException("Empty or oversized Waters spectrum.");
            }
            int columns = counts[0]; ImportGuard.Dimensions(rows, columns);
            if (counts.Any(c => c != columns)) throw new FormatException("Waters spectra have changing point counts; sparse or changing wavelength grids are not supported.");
            // Only the documented six-byte wavelength/absorbance representation is supported.
            if (data.Length != points * 6) throw new FormatException("Unsupported Waters PDA encoding; expected six bytes per wavelength/absorbance pair.");
            var waves = new double[columns]; var values = new double[rows, columns];
            for (int t = 0; t < rows; t++)
            {
                token.ThrowIfCancellationRequested();
                if (offsets[t] != (long)t * columns * 6) throw new FormatException("Unsupported Waters scan offsets or truncated data.");
                data.Position = offsets[t];
                for (int w = 0; w < columns; w++)
                {
                    if ((w & 1023) == 0) token.ThrowIfCancellationRequested();
                    short mantissa = dr.ReadInt16(); uint packed = dr.ReadUInt32();
                    double wavelength = (packed >> 9) * Math.Pow(2, (int)((packed >> 4) & 31) - 23);
                    if (t == 0) waves[w] = wavelength;
                    else if (wavelength != waves[w]) throw new FormatException("Changing Waters wavelength grids are not supported; no binning or resampling was applied.");
                    values[t, w] = mantissa * Math.Pow(4, packed & 15);
                }
                progress?.Report((t + 1d) * 90 / rows);
            }
            var metadata = new DataMetadata("Waters MassLynx PDA", "Waters raw absorbance", $"Function {function}; physical absorbance scale unconfirmed", Path.GetFullPath(path), ImportGuard.Fingerprint(path, idx, inf));
            var result = ImportGuard.Finish(times, waves, values, metadata, token); progress?.Report(100); return result;
        }
        catch (EndOfStreamException ex) { throw new FormatException("Waters PDA data are truncated.", ex); }
    }
}
