using System.Text;
using DADViewer.Domain;
namespace DADViewer.Infrastructure.Import;

public sealed class SpectralTextReader
{
    public DADData Read(string path, bool empower, CancellationToken token = default, IProgress<double>? progress = null)
    {
        using var stream = ImportGuard.Open(path);
        if (stream.Length > 256L * 1024 * 1024) throw new FormatException("Text import exceeds the 256 MiB input limit.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 65536, leaveOpen: true);
        int lineNumber = 0;
        string Next()
        {
            token.ThrowIfCancellationRequested(); string line = reader.ReadLine() ?? throw new FormatException("Spectral text header is incomplete.");
            lineNumber++; if (line.Length > 16_000_000) throw new FormatException("Text line is too long."); return line;
        }
        static string[] Cells(string line) => line.TrimEnd('\t', ' ').Split('\t').Select(s => s.Trim().Trim('"')).ToArray();
        bool comma = false; double timeFactor = 1; int skip; double[] waves; string sourceUnit; string format;
        int? expectedRows = null;
        if (empower)
        {
            var names = Cells(Next()); var metadata = Cells(Next());
            if (names.Length != metadata.Length) throw new FormatException("Waters Empower metadata columns do not match.");
            int unitIndex = Array.IndexOf(names, "Det. Units"), typeIndex = Array.IndexOf(names, "Channel Type"), sourceIndex = Array.IndexOf(names, "Source S/W Info");
            if (unitIndex < 0 || typeIndex < 0 || sourceIndex < 0 || !metadata[typeIndex].Contains("3D PDA", StringComparison.OrdinalIgnoreCase) || !metadata[sourceIndex].Contains("Empower", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Expected a Waters Empower 3D PDA ASCII export (.arw), not a single-channel or MS export.");
            sourceUnit = metadata[unitIndex]; var header = Cells(Next());
            if (header.Length < 3 || header[0] != "Wavelength") throw new FormatException("Waters Empower wavelength header is missing.");
            waves = header.Skip(1).Select(v => ImportGuard.Number(v)).ToArray();
            if (Next().Trim() != "Time") throw new FormatException("Waters Empower time header is missing.");
            // Empower ARW stores retention times in minutes; confirm against its sampling-rate metadata in validation.
            skip = 1; format = "Waters Empower PDA ASCII";
        }
        else
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); bool found = false;
            while (lineNumber < 256)
            {
                string line = Next(); if (line.Trim() is "Raw Data:" or "Data:") { found = true; break; }
                var cells = Cells(line); if (cells.Length == 2) metadata[cells[0]] = cells[1];
            }
            if (!found || !metadata.TryGetValue("Generating Data System", out string? system) || !system.Contains("Chromeleon", StringComparison.OrdinalIgnoreCase))
                throw new FormatException("Expected a Chromeleon spectral-field ASCII export with a Raw Data section.");
            if (!metadata.TryGetValue("Detector", out string? detector) || !detector.Equals("UV", StringComparison.OrdinalIgnoreCase) || !metadata.ContainsKey("Scan Min. (nm)"))
                throw new FormatException("Chromeleon import requires a full UV spectral field with wavelength coordinates. Single-channel chromatograms are not supported.");
            comma = metadata.TryGetValue("Dilution Factor", out string? dilution) && dilution.Contains(',');
            var units = metadata.Keys.Where(k => k.StartsWith("Signal Min. (", StringComparison.Ordinal)).Select(k => k[13..].TrimEnd(')')).ToArray();
            if (units.Length != 1) throw new FormatException("Chromeleon intensity unit is missing or ambiguous.");
            sourceUnit = units[0];
            if (metadata.TryGetValue("Spectra", out string? spectra))
            {
                double declared = ImportGuard.Number(spectra, comma);
                if (declared <= 0 || declared > DADDataRepository.MaxAxisLength || declared != Math.Truncate(declared)) throw new FormatException("Invalid declared spectrum count.");
                expectedRows = (int)declared;
            }
            var header = Cells(Next());
            if (header.Length < 4 || !header[1].StartsWith("Integr.Time", StringComparison.Ordinal)) throw new FormatException("Invalid Chromeleon spectral matrix header.");
            timeFactor = header[0] switch { "Time (min)" => 1, "Time (s)" => 1d / 60, _ => throw new FormatException("Unrecognized Chromeleon time unit.") };
            waves = header.Skip(2).Select(v => ImportGuard.Number(v, comma)).ToArray(); skip = 2; format = "Chromeleon spectral ASCII";
        }
        ImportGuard.Dimensions(1, waves.Length); var unit = ImportGuard.Unit(sourceUnit);
        int dataLine = lineNumber; int rows = 0;
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(line)) continue;
            rows++; ImportGuard.Dimensions(rows, waves.Length);
        }
        ImportGuard.Dimensions(rows, waves.Length);
        if (expectedRows is { } expected && expected != rows) throw new FormatException($"Chromeleon declares {expected} spectra but contains {rows} rows.");
        stream.Position = 0; reader.DiscardBufferedData();
        for (int i = 0; i < dataLine; i++) _ = Next();
        var times = new double[rows]; var values = new double[rows, waves.Length]; int t = 0;
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested(); if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.Length > 16_000_000) throw new FormatException("Text data line is too long.");
            var fields = Cells(line);
            if (fields.Length != waves.Length + skip) throw new FormatException($"Spectral row {t + 1} has {fields.Length} fields; expected {waves.Length + skip}.");
            times[t] = ImportGuard.Number(fields[0], comma) * timeFactor;
            if (skip == 2 && ImportGuard.Number(fields[1], comma) < 0) throw new FormatException("Negative integration time.");
            for (int w = 0; w < waves.Length; w++) { if ((w & 1023) == 0) token.ThrowIfCancellationRequested(); values[t, w] = ImportGuard.Number(fields[w + skip], comma) * unit.Factor; }
            t++; progress?.Report(t * 90d / rows);
        }
        var result = ImportGuard.Finish(times, waves, values, new(format, unit.Unit, $"ASCII source unit {sourceUnit}; times in minutes", Path.GetFullPath(path), ImportGuard.Fingerprint(path)), token);
        progress?.Report(100); return result;
    }
}
