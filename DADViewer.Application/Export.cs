using System.Globalization;
using DADViewer.Domain;
namespace DADViewer.Application;

public enum SliceKind { Chromatogram, Spectrum }
public sealed record ExportSettings(string SourceFile, int TimeIndex, int WavelengthIndex,
    double ColorMinimum, double ColorMaximum, int ColorSteps, string ColorScheme);
public interface IFileExportService { Task SaveAsync(string path, Action<Stream> write); }
public static class CsvExport
{
    // Invariant round-trip doubles preserve original values under every UI culture.
    public static void Write(TextWriter writer, DADData data, SliceKind kind, ExportSettings settings)
    {
        _ = data.GetIntensity(settings.TimeIndex, settings.WavelengthIndex);
        writer.WriteLine("time_min,wavelength_nm,intensity,source_file,color_minimum,color_maximum,color_steps,color_scheme");
        string source = Path.GetFileName(settings.SourceFile);
        if (source.Length > 0 && "=+-@\t\r\n".Contains(source[0])) source = "'" + source;
        static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        int count = kind == SliceKind.Spectrum ? data.NWaves : data.NSpect;
        for (int i = 0; i < count; i++)
        {
            int t = kind == SliceKind.Spectrum ? settings.TimeIndex : i;
            int w = kind == SliceKind.Spectrum ? i : settings.WavelengthIndex;
            writer.WriteLine(string.Join(',', Number(data.TimeStamps[t]), Number(data.Wavelengths[w]), Number(data.GetIntensity(t, w)),
                Quote(source), Number(settings.ColorMinimum), Number(settings.ColorMaximum), settings.ColorSteps.ToString(CultureInfo.InvariantCulture), Quote(settings.ColorScheme)));
        }
    }
}
