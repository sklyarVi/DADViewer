using System;
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
    public DAD2DView()
    {
        InitializeComponent();
        var transformGroup = new TransformGroup();
        var scaleTransform = new ScaleTransform(1, 1);
        var translateTransform = new TranslateTransform(0, 0);
        transformGroup.Children.Add(scaleTransform);
        transformGroup.Children.Add(translateTransform);
        ContainerGrid.RenderTransform = transformGroup;
        ContainerGrid.MouseWheel += ContainerGrid_MouseWheel;
    }

    private DADData currentDadData;
    private Ellipse? currentMarker;

    public string CurrentColorScheme { get; set; } = "Blue-Red";
    public int NumberOfColors { get; set; } = 16;
    public double[]? CustomColorSteps { get; set; } = null;
    public void RenderDadData(DADData dadData)
    {
        currentDadData = dadData;
        // Determine the dimensions of the bitmap (width corresponds to nWaves, height to nSpect)
        var width = dadData.NWaves;
        var height = dadData.NSpect;
        
        // Create a bitmap with a resolution of 96 dpi, in BGRA32 format
        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        
        // Prepare an array of pixels (each pixel is 4 bytes)
        var pixels = new int[width * height];
        
        // Find the minimum and maximum intensity in the data so you can map the values to the 0-255 scale
        var minIntensity = double.MaxValue;
        var maxIntensity = double.MinValue;
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                double intensity = dadData.Intensities[i, j];
                if (intensity < minIntensity) minIntensity = intensity;
                if (intensity > maxIntensity) maxIntensity = intensity;
            }
        }
        var range = maxIntensity - minIntensity;
        if (range == 0)
            range = 1; // prevent division by zero
        
        // Mapping intensity to color values (here simple grayscale)
        for (int i = 0; i < height; i++)
        {
            for (int j = 0; j < width; j++)
            {
                double intensity = dadData.Intensities[i, j];
                // Normalized value 0-1
                double norm = (intensity - minIntensity) / range;
                //Get the color according to the settings
                if (NumberOfColors > 1)
                {
                    double stepSize = 1.0 / (NumberOfColors - 1);
                    norm = Math.Round(norm / stepSize) * stepSize;
                    norm = Math.Min(1, Math.Max(0, norm));
                }
                Color color = Helpers.ColorMapHelper.GetColor(norm, NumberOfColors, CurrentColorScheme);
                int pixelIndex = i * width + j;
                // BGRA32 format: set full opacity (alpha = 255) and the same value for B, G and R
                pixels[pixelIndex] = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
            }
        }
        
        //  Move the pixel array to a bitmap
        Int32Rect rect = new Int32Rect(0, 0, width, height);
        bitmap.WritePixels(rect, pixels, width * 4, 0);
        
        // Set the generated bitmap as the source for the Image control
        ImageDisplay.Source = bitmap;
    }

    // Image click support
    private void ImageDisplay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(ImageDisplay);
        
        if (ImageDisplay.Source is not WriteableBitmap bitmap) return;
        var scaleX = bitmap.PixelWidth / ImageDisplay.ActualWidth;
        var scaleY = bitmap.PixelHeight / ImageDisplay.ActualHeight;
        var columnIndex = (int)(pos.X * scaleX);
        var rowIndex  = (int)(pos.Y * scaleY);

        DataPointSelected?.Invoke(this, new DataPointSelectedEventArgs(columnIndex, rowIndex));
        ShowMarkerAt(columnIndex, rowIndex);
    }
    
    private void ImageDisplay_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(ImageDisplay);
        if (ImageDisplay.Source is WriteableBitmap bitmap)
        {
            double scaleX = bitmap.PixelWidth / ImageDisplay.ActualWidth;
            double scaleY = bitmap.PixelHeight / ImageDisplay.ActualHeight;
            int col = (int)(pos.X * scaleX);
            int row = (int)(pos.Y * scaleY);
            
            if (currentDadData != null && row < currentDadData.NSpect && col < currentDadData.NWaves)
            {
                double time = currentDadData.TimeStamps[row];
                double wave = currentDadData.Wavelengths[col];
                double intensity = currentDadData.Intensities[row, col];
                ImageDisplay.ToolTip = $"Time: {time:F2}\n Wavelength: {wave:F2}\n Intensity: {intensity:F2}";
            }
        }
    }
    
    private void ShowMarkerAt(int col, int row)
    {
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
        
        if (ImageDisplay.Source is WriteableBitmap bitmap)
        {
            double scaleX = ImageDisplay.ActualWidth / bitmap.PixelWidth;
            double scaleY = ImageDisplay.ActualHeight / bitmap.PixelHeight;
            double x = col * scaleX - currentMarker.Width / 2;
            double y = row * scaleY - currentMarker.Height / 2;

            Canvas.SetLeft(currentMarker, x);
            Canvas.SetTop(currentMarker, y);
            MarkerCanvas.Children.Add(currentMarker);
        }
        currentMarkerRow = row;
        currentMarkerColumn = col;
    }
    
    public void UpdateMarkerByWavelength(int newColumnIndex)
    {
        
        var row = (currentMarkerRow >= 0) ? currentMarkerRow : (currentDadData != null ? currentDadData.NSpect / 2 : 0);
        ShowMarkerAt(newColumnIndex, row);
    }
    private int currentMarkerRow = -1;
    private int currentMarkerColumn = -1;

    
    public class DataPointSelectedEventArgs(int columnIndex, int rowIndex) : EventArgs
    {
        public int ColumnIndex { get; } = columnIndex;
        public int RowIndex { get; } = rowIndex;
    }
    public event EventHandler<DataPointSelectedEventArgs>? DataPointSelected;

    private void ImageDisplay_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ImageDisplay.RenderTransform is TransformGroup group &&
            group.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform scale)
        {
            double zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            scale.ScaleX *= zoomFactor;
            scale.ScaleY *= zoomFactor;
        }
    }
    
    private void ContainerGrid_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ContainerGrid.RenderTransform is TransformGroup group &&
            group.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform scale)
        {
            double zoomFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            scale.ScaleX *= zoomFactor;
            scale.ScaleY *= zoomFactor;
        }
    }
    
    private void ScrollViewer2D_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        if (ContainerGrid.RenderTransform is TransformGroup group &&
            group.Children.OfType<ScaleTransform>().FirstOrDefault() is ScaleTransform scale)
        {
            double zoomFactor = (e.Delta > 0) ? 1.1 : 1 / 1.1;
            scale.ScaleX *= zoomFactor;
            scale.ScaleY *= zoomFactor;
        }
    }

}