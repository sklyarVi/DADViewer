using System.Globalization;
using System.Windows;
using System.Windows.Media;
namespace DADViewer.Presentation.Views;

public abstract class PlotSurface : FrameworkElement
{
    protected Rect PlotArea => new(66, 28, Math.Max(1, ActualWidth - 90), Math.Max(1, ActualHeight - 78));
    protected static double Fraction(double value, double min, double max) => max == min ? 0.5 : (value - min) / (max - min);
    protected void Text(DrawingContext dc, string text, double x, double y, Brush? brush = null, double size = 11)
        => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush ?? Brushes.DimGray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, y));
    protected void Axes(DrawingContext dc, double xmin, double xmax, double ymin, double ymax, string xlabel, string ylabel)
    {
        var r = PlotArea;
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        Text(dc, ylabel, r.Left, 5, Brushes.Black, 12);
        for (int i = 0; i <= 4; i++)
        {
            double f = i / 4d, x = r.Left + r.Width * f, y = r.Bottom - r.Height * f;
            dc.DrawLine(new Pen(Brushes.LightGray, 0.5), new Point(x, r.Top), new Point(x, r.Bottom));
            dc.DrawLine(new Pen(Brushes.LightGray, 0.5), new Point(r.Left, y), new Point(r.Right, y));
            Text(dc, (xmin + (xmax - xmin) * f).ToString("G4"), x - 14, r.Bottom + 5);
            Text(dc, (ymin + (ymax - ymin) * f).ToString("G4"), 3, y - 7);
        }
        dc.DrawRectangle(null, new Pen(Brushes.SlateGray, 1), r);
        Text(dc, xlabel, r.Left + r.Width / 2 - 35, r.Bottom + 26, Brushes.Black);
    }
}
