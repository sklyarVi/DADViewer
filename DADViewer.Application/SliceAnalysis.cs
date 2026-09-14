using System.Globalization;
using System.Text;
using DADViewer.Domain;
namespace DADViewer.Application;

public sealed record AnalysisRequest(DADData Data, string Source, bool Spectrum, double Coordinate, PeakOptions Options, DADData? Reference = null, string ReferenceSource = "");
public sealed record AnalysisSnapshot(AnalysisRequest Request, double Coordinate, IReadOnlyList<double> X, double[] Y, PeakAnalysisResult Peaks, double? ReferenceCoordinate, IReadOnlyList<double>? ReferenceX, double[]? ReferenceY, ComparisonResult? Comparison, string? ComparisonUnavailableReason = null)
{
    public string AxisUnit => Request.Spectrum ? "nm" : "min";
    public string FixedUnit => Request.Spectrum ? "min" : "nm";
}
public static class SliceAnalysis
{
    public static AnalysisSnapshot Analyze(AnalysisRequest request, CancellationToken token = default)
    {
        var d = request.Data;
        var fixedAxis = request.Spectrum ? d.TimeStamps : d.Wavelengths;
        if (!double.IsFinite(request.Coordinate) || request.Coordinate < fixedAxis[0] || request.Coordinate > fixedAxis[^1])
            throw new ArgumentException($"Slice coordinate must be within {fixedAxis[0]:G6} … {fixedAxis[^1]:G6}.");
        int index = request.Spectrum ? d.FindTime(request.Coordinate) : d.FindWavelength(request.Coordinate);
        var x = request.Spectrum ? d.Wavelengths : d.TimeStamps;
        var y = request.Spectrum ? d.GetSpectrum(index) : d.GetChromatogram(index);
        var peaks = PeakAnalysis.Analyze(x, y, request.Options, token);
        double? referenceCoordinate = null; IReadOnlyList<double>? rx = null; double[]? ry = null; ComparisonResult? comparison = null;
        string? reason = null;
        if (request.Reference is { } reference)
        {
            var axis = request.Spectrum ? reference.TimeStamps : reference.Wavelengths;
            double coordinate = fixedAxis[index];
            if (d.Metadata.IntensityUnit != reference.Metadata.IntensityUnit)
                reason = $"Incompatible intensity units: {d.Metadata.IntensityUnit} vs {reference.Metadata.IntensityUnit}. Overlay and error metrics are unavailable.";
            else if (coordinate >= axis[0] && coordinate <= axis[^1])
            {
                int ri = request.Spectrum ? reference.FindTime(coordinate) : reference.FindWavelength(coordinate);
                referenceCoordinate = axis[ri]; rx = request.Spectrum ? reference.Wavelengths : reference.TimeStamps;
                ry = request.Spectrum ? reference.GetSpectrum(ri) : reference.GetChromatogram(ri);
                if (!double.IsFinite(Math.Max(y.Max(), ry.Max()) - Math.Min(y.Min(), ry.Min()))) throw new ArithmeticException("Combined intensity range is too large to display.");
                comparison = SignalComparison.Compare(x, y, rx, ry, token);
            }
        }
        return new(request, fixedAxis[index], x, y, peaks, referenceCoordinate, rx, ry, comparison, reason);
    }
}
public static class AnalysisCsv
{
    public static void Write(Stream stream, AnalysisSnapshot result, bool comparison)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        string Q(string value)
        {
            if (value.Length > 0 && "=+-@\t\r\n".Contains(value[0])) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var r = result.Request;
        string metadata = $"{Q(DataSourceName.Short(r.Source))},{(r.Spectrum ? "Spectrum" : "Chromatogram")},{N(result.Coordinate)},{result.FixedUnit},{result.AxisUnit}";
        if (comparison)
        {
            writer.WriteLine("source,slice,coordinate,coordinate_unit,axis_unit,reference_source,reference_coordinate,x,primary,reference_interpolated,difference,intensity_unit,primary_format,reference_format");
            if (result.Comparison is null) return;
            foreach (var p in result.Comparison.Points)
                writer.WriteLine($"{metadata},{Q(DataSourceName.Short(r.ReferenceSource))},{N(result.ReferenceCoordinate!.Value)},{N(p.X)},{N(p.Primary)},{N(p.Reference)},{N(p.Difference)},{Q(r.Data.Metadata.IntensityUnit)},{Q(r.Data.Metadata.Format)},{Q(r.Reference!.Metadata.Format)}");
        }
        else
        {
            writer.WriteLine("source,slice,coordinate,coordinate_unit,axis_unit,baseline,negative,min_local_prominence,min_distance,maximum_peaks,detected_peaks,shown_peaks,position,raw_intensity,height,local_prominence,left,right,signed_area,half_height_width,intensity_unit,source_format");
            string parameters = $"{metadata},{r.Options.Baseline},{r.Options.Negative},{N(r.Options.MinimumProminence)},{N(r.Options.MinimumDistance)},{r.Options.MaximumPeaks},{result.Peaks.DetectedCount},{result.Peaks.Peaks.Count}";
            if (result.Peaks.Peaks.Count == 0) writer.WriteLine(parameters + $",,,,,,,,,{Q(r.Data.Metadata.IntensityUnit)},{Q(r.Data.Metadata.Format)}");
            foreach (var p in result.Peaks.Peaks)
                writer.WriteLine($"{parameters},{N(p.Position)},{N(p.RawValue)},{N(p.Height)},{N(p.LocalProminence)},{N(p.Left)},{N(p.Right)},{N(p.Area)},{(p.HalfHeightWidth is { } width ? N(width) : "")},{Q(r.Data.Metadata.IntensityUnit)},{Q(r.Data.Metadata.Format)}");
        }
    }
}
