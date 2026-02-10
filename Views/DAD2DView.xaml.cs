using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DADViewer.Models;

namespace DADViewer.Views;

public partial class DAD2DView : UserControl
{
    private const int DefaultMax2DPixels = 2_000_000;
    private const double MinZoom = 1.0;
    private const double MaxZoom = 5.0;
    private const int VerticalGridDivisions = 8;
    private const int HorizontalGridDivisions = 8;

    private DADData? currentDadData;
    private Ellipse? currentMarker;
    private int currentMarkerRow = -1;
    private int currentMarkerColumn = -1;
    private ScaleTransform? scaleTransform;
    private TranslateTransform? translateTransform;
    private double zoomFactor = 1.0;
    private int[] renderedRowIndices = Array.Empty<int>();
    private int[] renderedColumnIndices = Array.Empty<int>();

    public DAD2DView()
    {
        InitializeComponent();
        var transformGroup = new TransformGroup();
        scaleTransform = new ScaleTransform(1, 1);
        translateTransform = new TranslateTransform(0, 0);
        transformGroup.Children.Add(scaleTransform);
        transformGroup.Children.Add(translateTransform);
        ContainerGrid.RenderTransform = transformGroup;
        ContainerGrid.MouseWheel += ContainerGrid_MouseWheel;
        SizeChanged += DAD2DView_SizeChanged;
    }

    public string CurrentColorScheme { get; set; } = "Blue-Red";
    public int NumberOfColors { get; set; } = 16;
    public int Max2DPixels { get; set; } = DefaultMax2DPixels;
    public int RenderedWidth { get; private set; }
    public int RenderedHeight { get; private set; }

    public void RenderDadData(DADData dadData)
    {
        currentDadData = dadData;

        int sourceWidth = dadData.NWaves;
        int sourceHeight = dadData.NSpect;
        int preferredWidth = (int)Math.Round(ImageDisplay.ActualWidth);
        int preferredHeight = (int)Math.Round(ImageDisplay.ActualHeight);
        if (preferredWidth < 320)
        {
            preferredWidth = Math.Max(900, sourceWidth * 6);
        }

        if (preferredHeight < 220)
        {
            preferredHeight = 560;
        }

        GetRenderDimensions(preferredWidth, preferredHeight, Max2DPixels, out int width, out int height);

        renderedColumnIndices = BuildSampleIndexArray(sourceWidth, width);
        renderedRowIndices = BuildSampleIndexArray(sourceHeight, height);
        RenderedWidth = width;
        RenderedHeight = height;

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new int[width * height];

        var minIntensity = double.MaxValue;
        var maxIntensity = double.MinValue;
        for (int i = 0; i < sourceHeight; i++)
        {
            for (int j = 0; j < sourceWidth; j++)
            {
                double intensity = dadData.Intensities[i, j];
                if (intensity < minIntensity)
                {
                    minIntensity = intensity;
                }

                if (intensity > maxIntensity)
                {
                    maxIntensity = intensity;
                }
            }
        }

        var range = maxIntensity - minIntensity;
        if (range == 0)
        {
            range = 1;
        }

        for (int y = 0; y < height; y++)
        {
            int sourceRow = renderedRowIndices[y];
            for (int x = 0; x < width; x++)
            {
                int sourceColumn = renderedColumnIndices[x];
                double intensity = dadData.Intensities[sourceRow, sourceColumn];

                double norm = (intensity - minIntensity) / range;
                if (NumberOfColors > 1)
                {
                    double stepSize = 1.0 / (NumberOfColors - 1);
                    norm = Math.Round(norm / stepSize) * stepSize;
                    norm = Math.Clamp(norm, 0, 1);
                }

                Color color = Helpers.ColorMapHelper.GetColor(norm, NumberOfColors, CurrentColorScheme);
                pixels[y * width + x] = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        ImageDisplay.Source = bitmap;
        UpdateZoomTransformCenter();
        DrawGridOverlay();

        if (currentMarkerRow >= 0 && currentMarkerColumn >= 0)
        {
            ShowMarkerAt(currentMarkerColumn, currentMarkerRow);
        }
    }

    public void UpdateMarkerByWavelength(int newColumnIndex)
    {
        int row = currentMarkerRow >= 0
            ? currentMarkerRow
            : (currentDadData != null ? currentDadData.NSpect / 2 : 0);

        UpdateMarkerByIndices(newColumnIndex, row);
    }

    public void UpdateMarkerByIndices(int columnIndex, int rowIndex)
    {
        if (currentDadData == null)
        {
            return;
        }

        int clampedColumn = Math.Clamp(columnIndex, 0, currentDadData.NWaves - 1);
        int clampedRow = Math.Clamp(rowIndex, 0, currentDadData.NSpect - 1);
        ShowMarkerAt(clampedColumn, clampedRow);
    }

    public void ExportCurrentImageAsPng(string filePath)
    {
        if (ImageDisplay.Source is not BitmapSource source)
        {
            throw new InvalidOperationException("No rendered 2D image to export.");
        }

        double outputWidth = ImageDisplay.ActualWidth;
        double outputHeight = ImageDisplay.ActualHeight;
        if (outputWidth < 2 || outputHeight < 2)
        {
            outputWidth = 1400;
            outputHeight = 900;
        }

        int pixelWidth = Math.Max(1, (int)Math.Ceiling(outputWidth));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(outputHeight));

        var renderTarget = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, outputWidth, outputHeight));
            dc.DrawImage(source, new Rect(0, 0, outputWidth, outputHeight));
        }

        renderTarget.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));

        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

    private void ImageDisplay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (currentDadData == null || ImageDisplay.Source is not WriteableBitmap bitmap)
        {
            return;
        }

        if (ImageDisplay.ActualWidth <= 0 || ImageDisplay.ActualHeight <= 0)
        {
            return;
        }

        var pos = e.GetPosition(ImageDisplay);
        int renderedColumn = Math.Clamp((int)(Math.Clamp(pos.X, 0, ImageDisplay.ActualWidth - 1) * bitmap.PixelWidth / ImageDisplay.ActualWidth), 0, bitmap.PixelWidth - 1);
        int renderedRow = Math.Clamp((int)(Math.Clamp(pos.Y, 0, ImageDisplay.ActualHeight - 1) * bitmap.PixelHeight / ImageDisplay.ActualHeight), 0, bitmap.PixelHeight - 1);

        int sourceColumn = renderedColumnIndices[renderedColumn];
        int sourceRow = renderedRowIndices[renderedRow];

        DataPointSelected?.Invoke(this, new DataPointSelectedEventArgs(sourceColumn, sourceRow));
        ShowMarkerAt(sourceColumn, sourceRow);
    }

    private void ImageDisplay_MouseMove(object sender, MouseEventArgs e)
    {
        if (currentDadData == null || ImageDisplay.Source is not WriteableBitmap bitmap)
        {
            return;
        }

        if (ImageDisplay.ActualWidth <= 0 || ImageDisplay.ActualHeight <= 0)
        {
            return;
        }

        var pos = e.GetPosition(ImageDisplay);
        int renderedColumn = Math.Clamp((int)(Math.Clamp(pos.X, 0, ImageDisplay.ActualWidth - 1) * bitmap.PixelWidth / ImageDisplay.ActualWidth), 0, bitmap.PixelWidth - 1);
        int renderedRow = Math.Clamp((int)(Math.Clamp(pos.Y, 0, ImageDisplay.ActualHeight - 1) * bitmap.PixelHeight / ImageDisplay.ActualHeight), 0, bitmap.PixelHeight - 1);

        int sourceColumn = renderedColumnIndices[renderedColumn];
        int sourceRow = renderedRowIndices[renderedRow];

        double time = currentDadData.TimeStamps[sourceRow];
        double wave = currentDadData.Wavelengths[sourceColumn];
        double intensity = currentDadData.Intensities[sourceRow, sourceColumn];
        ImageDisplay.ToolTip = $"Time: {time:F2}\nWavelength: {wave:F2}\nIntensity: {intensity:F2}";
    }

    private void ShowMarkerAt(int sourceColumn, int sourceRow)
    {
        if (currentDadData == null || ImageDisplay.Source is not WriteableBitmap bitmap)
        {
            return;
        }

        if (currentMarker != null)
        {
            MarkerCanvas.Children.Remove(currentMarker);
        }

        currentMarker = new Ellipse
        {
            Width = 10,
            Height = 10,
            Stroke = Brushes.Yellow,
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };

        int renderedColumn = FindNearestRenderedIndex(renderedColumnIndices, sourceColumn);
        int renderedRow = FindNearestRenderedIndex(renderedRowIndices, sourceRow);

        double scaleX = ImageDisplay.ActualWidth / bitmap.PixelWidth;
        double scaleY = ImageDisplay.ActualHeight / bitmap.PixelHeight;
        double x = (renderedColumn + 0.5) * scaleX - currentMarker.Width / 2;
        double y = (renderedRow + 0.5) * scaleY - currentMarker.Height / 2;

        Canvas.SetLeft(currentMarker, x);
        Canvas.SetTop(currentMarker, y);
        MarkerCanvas.Children.Add(currentMarker);

        currentMarkerRow = sourceRow;
        currentMarkerColumn = sourceColumn;
    }

    private void ContainerGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ApplyZoom(e.Delta > 0 ? 1.1 : 1 / 1.1);
    }

    private void ScrollViewer2D_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        ApplyZoom(e.Delta > 0 ? 1.1 : 1 / 1.1);
    }

    private static void GetRenderDimensions(int preferredWidth, int preferredHeight, int maxPixels, out int width, out int height)
    {
        width = Math.Max(1, preferredWidth);
        height = Math.Max(1, preferredHeight);

        long pixelCount = (long)width * height;
        if (pixelCount <= maxPixels)
        {
            return;
        }
        double scale = Math.Sqrt((double)maxPixels / pixelCount);
        width = Math.Max(1, (int)Math.Floor(width * scale));
        height = Math.Max(1, (int)Math.Floor(height * scale));
    }

    private void DAD2DView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (currentDadData == null)
        {
            return;
        }

        bool widthChanged = Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 20;
        bool heightChanged = Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 20;
        if (!widthChanged && !heightChanged)
        {
            return;
        }

        UpdateZoomTransformCenter();
        RenderDadData(currentDadData);
    }

    private void ApplyZoom(double zoomStep)
    {
        if (scaleTransform == null)
        {
            return;
        }

        double newZoom = Math.Clamp(zoomFactor * zoomStep, MinZoom, MaxZoom);
        if (Math.Abs(newZoom - zoomFactor) < 0.0001)
        {
            return;
        }

        zoomFactor = newZoom;
        scaleTransform.ScaleX = zoomFactor;
        scaleTransform.ScaleY = zoomFactor;
        UpdateZoomTransformCenter();

        if (translateTransform != null)
        {
            translateTransform.X = 0;
            translateTransform.Y = 0;
        }
    }

    private void UpdateZoomTransformCenter()
    {
        if (scaleTransform == null)
        {
            return;
        }

        double centerX = ImageDisplay.ActualWidth / 2;
        double centerY = ImageDisplay.ActualHeight / 2;
        if (centerX <= 0 || centerY <= 0)
        {
            centerX = ActualWidth / 2;
            centerY = ActualHeight / 2;
        }

        scaleTransform.CenterX = centerX;
        scaleTransform.CenterY = centerY;
    }

    private void DrawGridOverlay()
    {
        GridCanvas.Children.Clear();

        if (currentDadData == null || ImageDisplay.Source == null)
        {
            return;
        }

        double width = ImageDisplay.ActualWidth;
        double height = ImageDisplay.ActualHeight;
        if (width < 20 || height < 20)
        {
            return;
        }

        var border = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = new SolidColorBrush(Color.FromArgb(220, 85, 99, 120)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(border, 0);
        Canvas.SetTop(border, 0);
        GridCanvas.Children.Add(border);

        DrawVerticalGrid(width, height);
        DrawHorizontalGrid(width, height);
        DrawAxisTitles(width, height);
    }

    private void DrawVerticalGrid(double width, double height)
    {
        int tickCount = Math.Max(2, VerticalGridDivisions);
        for (int tick = 0; tick <= tickCount; tick++)
        {
            double x = tick * width / tickCount;

            var line = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = height,
                Stroke = new SolidColorBrush(Color.FromArgb(65, 80, 95, 122)),
                StrokeThickness = 1
            };
            GridCanvas.Children.Add(line);

            int renderedIndex = Math.Clamp((int)Math.Round((double)tick / tickCount * (renderedColumnIndices.Length - 1)), 0, renderedColumnIndices.Length - 1);
            int sourceColumn = renderedColumnIndices[renderedIndex];
            double wave = currentDadData!.Wavelengths[sourceColumn];
            AddOverlayLabel($"{wave:F0}", x + 2, height - 18);
        }
    }

    private void DrawHorizontalGrid(double width, double height)
    {
        int tickCount = Math.Max(2, HorizontalGridDivisions);
        for (int tick = 0; tick <= tickCount; tick++)
        {
            double y = tick * height / tickCount;

            var line = new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = width,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb(65, 80, 95, 122)),
                StrokeThickness = 1
            };
            GridCanvas.Children.Add(line);

            int renderedIndex = Math.Clamp((int)Math.Round((double)tick / tickCount * (renderedRowIndices.Length - 1)), 0, renderedRowIndices.Length - 1);
            int sourceRow = renderedRowIndices[renderedIndex];
            double time = currentDadData!.TimeStamps[sourceRow];
            AddOverlayLabel($"{time:F2}", 2, y - 10);
        }
    }

    private void DrawAxisTitles(double width, double height)
    {
        AddOverlayLabel("Wavelength (nm)", width / 2 - 46, height - 34, fontSize: 11, bold: true);

        var timeLabel = new TextBlock
        {
            Text = "Time",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(41, 52, 69)),
            Background = new SolidColorBrush(Color.FromArgb(180, 250, 253, 255)),
            Padding = new Thickness(3, 1, 3, 1),
            RenderTransform = new RotateTransform(-90)
        };

        Canvas.SetLeft(timeLabel, 2);
        Canvas.SetTop(timeLabel, height / 2 + 20);
        GridCanvas.Children.Add(timeLabel);
    }

    private void AddOverlayLabel(string text, double x, double y, double fontSize = 10, bool bold = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = new SolidColorBrush(Color.FromRgb(41, 52, 69)),
            Background = new SolidColorBrush(Color.FromArgb(175, 250, 253, 255)),
            Padding = new Thickness(2, 0, 2, 0)
        };

        Canvas.SetLeft(label, Math.Max(0, x));
        Canvas.SetTop(label, Math.Max(0, y));
        GridCanvas.Children.Add(label);
    }

    private static int[] BuildSampleIndexArray(int sourceLength, int renderedLength)
    {
        if (renderedLength <= 1)
        {
            return new[] { 0 };
        }

        var indices = new int[renderedLength];
        double ratio = (double)(sourceLength - 1) / (renderedLength - 1);
        for (int i = 0; i < renderedLength; i++)
        {
            indices[i] = Math.Clamp((int)Math.Round(i * ratio), 0, sourceLength - 1);
        }

        return indices;
    }

    private static int FindNearestRenderedIndex(int[] renderedIndices, int sourceIndex)
    {
        int foundIndex = Array.BinarySearch(renderedIndices, sourceIndex);
        if (foundIndex >= 0)
        {
            return foundIndex;
        }

        int insertionPoint = ~foundIndex;
        if (insertionPoint <= 0)
        {
            return 0;
        }

        if (insertionPoint >= renderedIndices.Length)
        {
            return renderedIndices.Length - 1;
        }

        int left = insertionPoint - 1;
        int right = insertionPoint;
        return Math.Abs(renderedIndices[left] - sourceIndex) <= Math.Abs(renderedIndices[right] - sourceIndex) ? left : right;
    }

    public class DataPointSelectedEventArgs(int columnIndex, int rowIndex) : EventArgs
    {
        public int ColumnIndex { get; } = columnIndex;
        public int RowIndex { get; } = rowIndex;
    }

    public event EventHandler<DataPointSelectedEventArgs>? DataPointSelected;
}
