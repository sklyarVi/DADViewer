namespace DADViewer.Application;

public sealed record ComparedPoint(double X, double Primary, double Reference, double Difference);
public sealed record ComparisonResult(IReadOnlyList<ComparedPoint> Points, double? MeanAbsoluteError, double? RootMeanSquareError);
public static class SignalComparison
{
    // Interpolate the reference only within its measured domain. No extrapolation,
    // retention-time alignment, baseline correction or amplitude normalization.
    public static ComparisonResult Compare(IReadOnlyList<double> x, IReadOnlyList<double> y, IReadOnlyList<double> referenceX, IReadOnlyList<double> referenceY, CancellationToken token = default)
    {
        PeakAnalysis.Validate(x,y,token); PeakAnalysis.Validate(referenceX,referenceY,token);
        var points = new List<ComparedPoint>(); int j = 0; double mean = 0, scale = 0, squares = 0;
        for (int i = 0; i < x.Count; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            if (x[i] < referenceX[0] || x[i] > referenceX[^1]) continue;
            while (j + 1 < referenceX.Count && referenceX[j + 1] < x[i]) j++;
            double value = referenceY[j];
            if (j + 1 < referenceX.Count)
            { double f = (x[i]-referenceX[j])/(referenceX[j+1]-referenceX[j]); value = referenceY[j]*(1-f)+referenceY[j+1]*f; }
            double diff = y[i] - value, abs = Math.Abs(diff);
            if (!double.IsFinite(diff)) throw new ArithmeticException("Comparison difference exceeds the supported numeric range.");
            points.Add(new(x[i], y[i], value, diff)); mean += (abs - mean) / points.Count;
            if (abs > 0) { if (abs > scale) { squares = 1 + squares * Math.Pow(scale / abs, 2); scale = abs; } else squares += Math.Pow(abs / scale, 2); }
        }
        return new(points.AsReadOnly(), points.Count == 0 ? null : mean, points.Count == 0 ? null : scale * Math.Sqrt(squares / points.Count));
    }
}
