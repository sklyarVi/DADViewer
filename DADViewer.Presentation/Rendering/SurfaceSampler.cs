using DADViewer.Domain;
namespace DADViewer.Presentation.Rendering;

public static class SurfaceSampler
{
    // Select actual measured coordinates. Endpoints and the global minimum/maximum
    // are mandatory; bucket extrema preserve local peaks and negative excursions.
    public static (int[] Times, int[] Wavelengths) Select(DADData data, int limit, CancellationToken token = default)
    {
        if (limit < 4) throw new ArgumentOutOfRangeException(nameof(limit));
        token.ThrowIfCancellationRequested();
        if (data.NSpect <= limit && data.NWaves <= limit)
            return (Enumerable.Range(0, data.NSpect).ToArray(), Enumerable.Range(0, data.NWaves).ToArray());
        var timeMin = Enumerable.Repeat(double.PositiveInfinity, data.NSpect).ToArray();
        var timeMax = Enumerable.Repeat(double.NegativeInfinity, data.NSpect).ToArray();
        var waveMin = Enumerable.Repeat(double.PositiveInfinity, data.NWaves).ToArray();
        var waveMax = Enumerable.Repeat(double.NegativeInfinity, data.NWaves).ToArray();
        int minT = 0, minW = 0, maxT = 0, maxW = 0;
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        long visited = 0;
        for (int t = 0; t < data.NSpect; t++)
            for (int w = 0; w < data.NWaves; w++)
            {
                if ((visited++ & 1023) == 0) token.ThrowIfCancellationRequested();
                double value = data.GetIntensity(t, w);
                timeMin[t] = Math.Min(timeMin[t], value); timeMax[t] = Math.Max(timeMax[t], value);
                waveMin[w] = Math.Min(waveMin[w], value); waveMax[w] = Math.Max(waveMax[w], value);
                if (value < minimum) { minimum = value; minT = t; minW = w; }
                if (value > maximum) { maximum = value; maxT = t; maxW = w; }
            }
        return (Choose(timeMin, timeMax, limit, minT, maxT, token), Choose(waveMin, waveMax, limit, minW, maxW, token));
    }
    private static int[] Choose(double[] minima, double[] maxima, int limit, int globalMin, int globalMax, CancellationToken token)
    {
        int length = minima.Length;
        if (length <= limit) return Enumerable.Range(0, length).ToArray();
        var indices = new SortedSet<int> { 0, length - 1, globalMin, globalMax };
        int buckets = (limit - 4) / 2;
        for (int b = 0; b < buckets; b++)
        {
            token.ThrowIfCancellationRequested();
            int start = 1 + (int)((long)b * (length - 2) / buckets);
            int end = 1 + (int)((long)(b + 1) * (length - 2) / buckets);
            int lo = start, hi = start;
            for (int i = start + 1; i < end; i++)
            { if (minima[i] < minima[lo]) lo = i; if (maxima[i] > maxima[hi]) hi = i; }
            indices.Add(lo); indices.Add(hi);
        }
        // Coincident extrema leave room for extra baseline samples. Split the
        // largest gaps, keeping the mesh bounded and deterministic on flat data.
        while (indices.Count < limit)
        {
            token.ThrowIfCancellationRequested();
            int previous = 0, bestStart = 0, bestGap = 0;
            foreach (int index in indices)
            { if (index - previous > bestGap) { bestGap = index - previous; bestStart = previous; } previous = index; }
            indices.Add(bestStart + bestGap / 2);
        }
        return indices.ToArray();
    }
}
