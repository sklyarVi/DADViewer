using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using DADViewer.Domain;
namespace DADViewer.Infrastructure.Import;

internal static class ImportGuard
{
    internal static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
    internal static void Dimensions(long rows, long columns)
    {
        if (rows <= 0 || columns <= 0 || rows > DADDataRepository.MaxAxisLength || columns > DADDataRepository.MaxAxisLength || rows * columns > DADDataRepository.MaxDataPoints)
            throw new FormatException($"Unsupported dimensions: {rows} × {columns}; limit {DADDataRepository.MaxDataPoints:N0} values.");
    }
    internal static uint BigUInt(BinaryReader reader) => BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
    internal static double BigDouble(BinaryReader reader) => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReverseEndianness(reader.ReadInt64()));
    internal static string Pascal(BinaryReader reader, long offset, bool unicode)
    {
        reader.BaseStream.Position = offset; int length = reader.ReadByte() * (unicode ? 2 : 1);
        var bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException();
        return (unicode ? Encoding.Unicode : Encoding.Latin1).GetString(bytes).Trim();
    }
    internal static (string Unit, double Factor) Unit(string source) => source.Trim() switch
    {
        "mAU" => ("mAU", 1), "AU" => ("mAU", 1000), "µAU" or "μAU" or "uAU" => ("mAU", 0.001),
        _ => throw new FormatException($"Unsupported absorbance unit '{source}'. A confirmed AU, mAU or µAU unit is required.")
    };
    internal static double Number(string text, bool comma = false)
    {
        text = text.Trim().Trim('"');
        if (comma) text = text.Replace(".", "").Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            throw new FormatException($"Invalid numeric value '{text[..Math.Min(60, text.Length)]}'.");
        return value;
    }
    internal static string Fingerprint(params string[] paths) => string.Join("|", paths.Select(path => { var file = new FileInfo(path); return $"{file.Name}:{file.Length}:{file.LastWriteTimeUtc.Ticks}"; }));
    internal static DADData Finish(double[] times, double[] waves, double[,] values, DataMetadata metadata, CancellationToken token)
    {
        try { return new(times, waves, values, token, metadata); }
        catch (ArgumentException ex) { throw new FormatException("Invalid imported axes or intensities: " + ex.Message, ex); }
    }
}
