namespace DADViewer.Application;

public enum BaselineMode { EndpointLine, Zero }
public sealed record PeakOptions(double MinimumProminence, double MinimumDistance, BaselineMode Baseline = BaselineMode.EndpointLine, bool Negative = false, int MaximumPeaks = 500);
public sealed record PeakResult(int Index, double Position, double RawValue, double Height, double LocalProminence, double Left, double Right, double Area, double? HalfHeightWidth);
public sealed record PeakAnalysisResult(IReadOnlyList<PeakResult> Peaks, double[] Baseline, int DetectedCount);

public static class PeakAnalysis
{
    public static PeakAnalysisResult Analyze(IReadOnlyList<double> x, IReadOnlyList<double> y, PeakOptions options, CancellationToken token = default)
    {
        Validate(x, y, token);
        if (!double.IsFinite(options.MinimumProminence) || options.MinimumProminence < 0 || !double.IsFinite(options.MinimumDistance) || options.MinimumDistance < 0 || options.MaximumPeaks is < 1 or > 10000 || !Enum.IsDefined(options.Baseline))
            throw new ArgumentException("Peak thresholds must be finite and non-negative; result limit must be 1–10000.");
        int n = x.Count, sign = options.Negative ? -1 : 1;
        var baseline = new double[n]; var signal = new double[n];
        for (int i = 0; i < n; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            double f = n == 1 ? 0 : (x[i] - x[0]) / (x[^1] - x[0]);
            baseline[i] = options.Baseline == BaselineMode.Zero ? 0 : y[0] * (1 - f) + y[^1] * f;
            signal[i] = (y[i] - baseline[i]) * sign;
            if (!double.IsFinite(signal[i])) throw new ArithmeticException("Baseline-corrected signal exceeds the supported numeric range.");
        }
        if (!double.IsFinite(signal.Max() - signal.Min())) throw new ArithmeticException("Corrected signal range is too large.");
        var candidates = new List<(int Index, int Left, int Right, double Prominence)>();
        for (int i = 1; i < n - 1; i++)
        {
            token.ThrowIfCancellationRequested();
            if (signal[i] <= signal[i - 1]) continue;
            int end = i; while (end + 1 < n && signal[end + 1] == signal[i]) { if ((end & 1023) == 0) token.ThrowIfCancellationRequested(); end++; }
            if (end == n - 1 || signal[end + 1] >= signal[i]) { i = end; continue; }
            int apex = (i + end) / 2, left = i - 1, right = end + 1;
            while (left > 0 && signal[left - 1] <= signal[left]) { if ((left & 1023) == 0) token.ThrowIfCancellationRequested(); left--; }
            while (right < n - 1 && signal[right + 1] <= signal[right]) { if ((right & 1023) == 0) token.ThrowIfCancellationRequested(); right++; }
            // Split an interior flat valley at its midpoint so neighboring areas do not overlap.
            if (left > 0)
            {
                int valleyEnd = left; while (valleyEnd + 1 < apex && signal[valleyEnd + 1] == signal[left]) { token.ThrowIfCancellationRequested(); valleyEnd++; }
                left = (left + valleyEnd) / 2;
            }
            if (right < n - 1)
            {
                int valleyStart = right; while (valleyStart - 1 > apex && signal[valleyStart - 1] == signal[right]) { token.ThrowIfCancellationRequested(); valleyStart--; }
                right = (valleyStart + right) / 2;
            }
            double prominence = signal[apex] - Math.Max(signal[left], signal[right]);
            if (signal[apex] > 0 && prominence > 0 && prominence >= options.MinimumProminence)
                candidates.Add((apex, left, right, prominence));
            i = end;
        }
        var suppressed = new bool[candidates.Count]; var selected = new List<int>();
        foreach (int c in Enumerable.Range(0, candidates.Count).OrderByDescending(i => signal[candidates[i].Index]).ThenBy(i => candidates[i].Index))
        {
            token.ThrowIfCancellationRequested(); if (suppressed[c]) continue; selected.Add(c);
            double position = x[candidates[c].Index];
            for (int j = c - 1; j >= 0 && position - x[candidates[j].Index] < options.MinimumDistance; j--) suppressed[j] = true;
            for (int j = c + 1; j < candidates.Count && x[candidates[j].Index] - position < options.MinimumDistance; j++) suppressed[j] = true;
        }
        var peaks = new List<PeakResult>();
        foreach (int c in selected.Take(options.MaximumPeaks).OrderBy(c => candidates[c].Index))
        {
            token.ThrowIfCancellationRequested(); var peak = candidates[c]; double area = 0;
            for (int i = peak.Left; i < peak.Right; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                double a = signal[i], b = signal[i + 1], dx = x[i + 1] - x[i];
                double average = a >= 0 && b >= 0 ? a / 2 + b / 2 : a <= 0 && b <= 0 ? 0 :
                    a > 0 ? a / 2 * (a / (a - b)) : b / 2 * (b / (b - a));
                area += dx * average;
            }
            double half = signal[peak.Index] / 2; int l = peak.Index, r = peak.Index;
            while (l > peak.Left && signal[l] > half) l--;
            while (r < peak.Right && signal[r] > half) r++;
            double? width = null;
            if (l < peak.Index && r > peak.Index && signal[l] <= half && signal[r] <= half)
            {
                double leftX = x[l] + (x[l + 1] - x[l]) * ((half - signal[l]) / (signal[l + 1] - signal[l]));
                double rightX = x[r - 1] + (x[r] - x[r - 1]) * ((signal[r - 1] - half) / (signal[r - 1] - signal[r]));
                width = rightX - leftX;
            }
            if (!double.IsFinite(area) || width is { } w && !double.IsFinite(w)) throw new ArithmeticException("Peak area or width exceeds the supported numeric range.");
            peaks.Add(new(peak.Index, x[peak.Index], y[peak.Index], signal[peak.Index], peak.Prominence, x[peak.Left], x[peak.Right], sign * area, width));
        }
        return new(peaks.AsReadOnly(), baseline, selected.Count);
    }
    internal static void Validate(IReadOnlyList<double> x, IReadOnlyList<double> y, CancellationToken token)
    {
        if (x.Count == 0 || x.Count != y.Count) throw new ArgumentException("Signal axes and values must have equal nonzero lengths.");
        for (int i = 0; i < x.Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            if (!double.IsFinite(x[i]) || !double.IsFinite(y[i]) || i > 0 && x[i] <= x[i - 1]) throw new ArgumentException("Signal coordinates must increase and all values must be finite.");
        }
        if (!double.IsFinite(x[^1] - x[0]) || !double.IsFinite(y.Max() - y.Min())) throw new ArgumentException("Signal range is too large.");
    }
}
