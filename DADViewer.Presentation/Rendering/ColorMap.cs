using System.Windows.Media;
namespace DADViewer.Presentation.Rendering;

public enum ColorScheme { Viridis, Jet, Grayscale }
public static class ColorMap
{
    // Published viridis anchors; linear interpolation between anchors.
    private static readonly Color[] Viridis = [Rgb(68, 1, 84), Rgb(72, 36, 117), Rgb(65, 68, 135), Rgb(53, 95, 141), Rgb(42, 120, 142), Rgb(33, 145, 140), Rgb(34, 168, 132), Rgb(68, 191, 112), Rgb(122, 209, 81), Rgb(189, 223, 38), Rgb(253, 231, 37)];
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
        double index = v * (Viridis.Length - 1);
        int i = Math.Min((int)index, Viridis.Length - 2);
        double f = index - i;
        var a = Viridis[i]; var b = Viridis[i + 1];
        return Rgb((byte)Math.Round(a.R + (b.R - a.R) * f), (byte)Math.Round(a.G + (b.G - a.G) * f), (byte)Math.Round(a.B + (b.B - a.B) * f));
    }
}
