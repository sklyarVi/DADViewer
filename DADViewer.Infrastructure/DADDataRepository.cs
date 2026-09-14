using DADViewer.Application;
using DADViewer.Domain;
namespace DADViewer.Infrastructure;

public sealed class DADDataRepository : IDADDataReader
{
    // 64 MiB matrix; immutable construction temporarily needs one additional copy.
    public const long MaxDataPoints = 8_000_000;
    public const int MaxAxisLength = 1_000_000;

    public DADData LoadFromFile(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 8) throw new FormatException("DAD header is incomplete (8 bytes required).");
        int waves = reader.ReadInt32(), spectra = reader.ReadInt32();
        if (waves <= 0 || spectra <= 0 || waves > MaxAxisLength || spectra > MaxAxisLength)
            throw new FormatException($"Invalid dimensions: {spectra} spectra, {waves} wavelengths.");
        long points = checked((long)waves * spectra);
        if (points > MaxDataPoints) throw new FormatException($"Dataset exceeds the limit of {MaxDataPoints:N0} values.");
        long expected = checked(8L + spectra * 8L + waves * 4L + points * 8L);
        if (stream.Length < expected) throw new FormatException($"Incomplete DAD file: expected at least {expected} bytes, found {stream.Length}.");
        var times = new double[spectra];
        var wavelengths = new double[waves];
        var intensities = new double[spectra, waves];
        try
        {
            for (int t = 0; t < spectra; t++)
            {
                if ((t & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                times[t] = reader.ReadDouble();
            }
            for (int w = 0; w < waves; w++)
            {
                if ((w & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                wavelengths[w] = reader.ReadSingle();
            }
            long done = 0;
            int lastPercent = -1;
            for (int t = 0; t < spectra; t++)
                for (int w = 0; w < waves; w++)
                {
                    if ((done & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    intensities[t, w] = reader.ReadDouble();
                    int percent = (int)(++done * 90 / points);
                    if (percent != lastPercent) { lastPercent = percent; progress?.Report(percent); }
                }
            var data = new DADData(times, wavelengths, intensities, cancellationToken);
            progress?.Report(100);
            return data;
        }
        catch (EndOfStreamException ex) { throw new FormatException("DAD file ended unexpectedly.", ex); }
        catch (ArgumentException ex) { throw new FormatException(ex.Message, ex); }
    }
}
