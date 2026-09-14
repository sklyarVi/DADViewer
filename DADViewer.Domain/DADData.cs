namespace DADViewer.Domain;

/// <summary>An immutable, validated matrix indexed by [time, wavelength].</summary>
public sealed class DADData
{
    private readonly double[] _times;
    private readonly double[] _waves;
    private readonly double[,] _intensities;
    public IReadOnlyList<double> TimeStamps { get; }
    public IReadOnlyList<double> Wavelengths { get; }
    public int NSpect => _times.Length;
    public int NWaves => _waves.Length;
    public DataMetadata Metadata { get; }
    public double MinIntensity { get; }
    public double MaxIntensity { get; }

    public DADData(double[] times, double[] waves, double[,] intensities, CancellationToken cancellationToken = default, DataMetadata? metadata = null)
    {
        Metadata = metadata ?? new();
        ArgumentNullException.ThrowIfNull(times);
        ArgumentNullException.ThrowIfNull(waves);
        ArgumentNullException.ThrowIfNull(intensities);
        if (times.Length == 0 || waves.Length == 0 || intensities.GetLength(0) != times.Length || intensities.GetLength(1) != waves.Length)
            throw new ArgumentException("Axes and matrix dimensions do not match.");
        _times = (double[])times.Clone();
        _waves = (double[])waves.Clone();
        ValidateAxis(_times, false, "Time");
        ValidateAxis(_waves, true, "Wavelength");
        _intensities = (double[,])intensities.Clone();
        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        for (int t = 0; t < NSpect; t++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int w = 0; w < NWaves; w++)
            {
                if ((w & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                double value = _intensities[t, w];
                if (!double.IsFinite(value)) throw new ArgumentException($"Non-finite intensity at [{t}, {w}].");
                min = Math.Min(min, value); max = Math.Max(max, value);
            }
        }
        if (!double.IsFinite(max - min)) throw new ArgumentException("Intensity range is too large to display.");
        MinIntensity = min; MaxIntensity = max;
        TimeStamps = Array.AsReadOnly(_times);
        Wavelengths = Array.AsReadOnly(_waves);
    }

    private static void ValidateAxis(double[] values, bool positive, string name)
    {
        for (int i = 0; i < values.Length; i++)
            if (!double.IsFinite(values[i]) || (positive ? values[i] <= 0 : values[i] < 0) || (i > 0 && values[i] <= values[i - 1]))
                throw new ArgumentException($"{name} axis must be finite, {(positive ? "positive" : "non-negative")} and strictly increasing (index {i}).");
    }

    public double GetIntensity(int timeIndex, int wavelengthIndex) => _intensities[timeIndex, wavelengthIndex];
    public double[] GetSpectrum(int timeIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(timeIndex, NSpect);
        var result = new double[NWaves];
        for (int w = 0; w < NWaves; w++) result[w] = _intensities[timeIndex, w];
        return result;
    }
    public double[] GetChromatogram(int wavelengthIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(wavelengthIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(wavelengthIndex, NWaves);
        var result = new double[NSpect];
        for (int t = 0; t < NSpect; t++) result[t] = _intensities[t, wavelengthIndex];
        return result;
    }
    public int FindTime(double value) => Nearest(_times, value);
    public int FindWavelength(double value) => Nearest(_waves, value);
    private static int Nearest(double[] axis, double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        int index = Array.BinarySearch(axis, value);
        if (index >= 0) return index;
        index = ~index;
        if (index == 0) return 0;
        if (index == axis.Length) return index - 1;
        return value - axis[index - 1] <= axis[index] - value ? index - 1 : index;
    }
}
