using System.Windows.Media;
namespace DADViewer.Presentation.Rendering;

public enum ColorScheme { Viridis, Jet, Grayscale }
public static class ColorMap
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    public static double Normalize(double value, double min, double max) => max == min ? 0.5 : Math.Clamp((value - min) / (max - min), 0, 1);
    public static Color Get(double value, int steps, ColorScheme scheme)
    {
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        if (steps is < 2 or > 256) throw new ArgumentOutOfRangeException(nameof(steps));
        double v = Math.Round(Math.Clamp(value, 0, 1) * (steps - 1)) / (steps - 1);
        if (scheme == ColorScheme.Grayscale) { byte c = (byte)Math.Round(v * 255); return Rgb(c, c, c); }
        if (scheme == ColorScheme.Jet)
        {
            static byte Channel(double c) => (byte)Math.Round(Math.Clamp(c, 0, 1) * 255);
            return Rgb(Channel(1.5 - Math.Abs(4 * v - 3)), Channel(1.5 - Math.Abs(4 * v - 2)), Channel(1.5 - Math.Abs(4 * v - 1)));
        }
        return ViridisPalette.Colors[(int)Math.Round(v * 255)];
    }
}
