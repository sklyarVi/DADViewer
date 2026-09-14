using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DADViewer.Presentation.Views;
namespace DADViewer.Presentation.Rendering;

public static class PngExport
{
    public static byte[] Capture(PlotSurface plot, string caption, ColorScale scale)
    {
        plot.UpdateLayout();
        if (plot.ActualWidth < 1 || plot.ActualHeight < 1 || !plot.IsVisible) throw new InvalidOperationException("Select the intensity map tab before exporting its image.");
        double width = Math.Max(800, plot.ActualWidth), height = plot.ActualHeight;
        const int footer = 150;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height + footer));
            dc.DrawRectangle(new VisualBrush(plot) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, plot.ActualWidth, height));
            var text = new FormattedText(caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.Black, 1)
                { MaxTextWidth = width - 32, MaxTextHeight = 100, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(text, new Point(16, height + 8));
            for (int i = 0; i < scale.Steps; i++)
            {
                double left = Math.Max(0, (i - 0.5) / (scale.Steps - 1)), right = Math.Min(1, (i + 0.5) / (scale.Steps - 1));
                dc.DrawRectangle(new SolidColorBrush(ColorMap.Get(i / (scale.Steps - 1d), scale.Steps, scale.Scheme)), null,
                    new Rect(16 + (width - 32) * left, height + 122, (width - 32) * (right - left) + 0.1, 16));
            }
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling((height + footer) * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var metadata = new BitmapMetadata("png"); metadata.SetQuery("/tEXt/{str=Description}", caption);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
}
