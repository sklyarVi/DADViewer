using System.Windows.Media;
namespace DADViewer.Presentation.Rendering;

public sealed record ColorScale
{
    public double Minimum { get; }
    public double Maximum { get; }
    public int Steps { get; }
    public ColorScheme Scheme { get; }
    public ColorScale(double minimum, double maximum, int steps, ColorScheme scheme)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum < minimum || !double.IsFinite(maximum - minimum))
            throw new ArgumentException("Color range must be finite, with minimum <= maximum.");
        if (steps is < 2 or > 256) throw new ArgumentOutOfRangeException(nameof(steps));
        Minimum = minimum; Maximum = maximum; Steps = steps; Scheme = scheme;
    }
    public Color Map(double value) => ColorMap.Get(ColorMap.Normalize(value, Minimum, Maximum), Steps, Scheme);
}
