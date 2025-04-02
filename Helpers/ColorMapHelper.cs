using System;
using System.Windows.Media;

namespace DADViewer.Helpers;

public class ColorMapHelper
{
    public static Color GetColor(double normalizedValue, int steps, string colorScheme)
    {
        // Example of a method that generates a color based on the value (0-1), assuming that 0 = blue, 1 = red
        normalizedValue = Math.Min(1, Math.Max(0, normalizedValue));
        switch (colorScheme)
            {
                case "Viridis":
                    // interpolate between purple and green
                    byte rV = (byte)(normalizedValue * 255);
                    byte gV = (byte)(normalizedValue * 200);
                    byte bV = (byte)(255 - normalizedValue * 255);
                    return Color.FromArgb(255, rV, gV, bV);
                case "Jet":
                    if (normalizedValue < 0.33)
                    {
                        byte rJ = (byte)(normalizedValue / 0.33 * 255);
                        return Color.FromArgb(255, rJ, 0, 255);
                    }
                    else if (normalizedValue < 0.66)
                    {
                        byte gJ = (byte)(((normalizedValue - 0.33) / 0.33) * 255);
                        return Color.FromArgb(255, 255, gJ, 0);
                    }
                    else
                    {
                        byte bJ = (byte)(((1 - normalizedValue) / 0.34) * 255);
                        return Color.FromArgb(255, 255, 255, bJ);
                    }
                case "Hot":
                    // black -> red -> yellow -> white
                    if (normalizedValue < 0.33)
                    {
                        byte rH = (byte)(normalizedValue / 0.33 * 255);
                        return Color.FromArgb(255, rH, 0, 0);
                    }
                    else if (normalizedValue < 0.66)
                    {
                        byte gH = (byte)(((normalizedValue - 0.33) / 0.33) * 255);
                        return Color.FromArgb(255, 255, gH, 0);
                    }
                    else
                    {
                        byte bH = (byte)(((normalizedValue - 0.66) / 0.34) * 255);
                        return Color.FromArgb(255, 255, 255, bH);
                    }
                case "Blue-Red":
                default:
                    byte red = (byte)(normalizedValue * 255);
                    byte blue = (byte)((1 - normalizedValue) * 255);
                    return Color.FromArgb(255, red, 0, blue);
            }
    }
}