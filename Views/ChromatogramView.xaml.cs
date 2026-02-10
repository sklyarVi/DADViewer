using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DADViewer.Models;

namespace DADViewer.Views;

public partial class ChromatogramView : UserControl
{
    private DADData? currentDADData;
    private int currentWavelengthIndex;
    private Polyline? polyline;
    private Ellipse? marker;
    private int currentSelectedTimeIndex = -1;

    public ChromatogramView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RedrawPlot();
    }

    public void RenderChromatogram(DADData data, int wavelengthIndex, int selectedTimeIndex)
    {
        if (wavelengthIndex < 0 || wavelengthIndex >= data.NWaves)
        {
            throw new ArgumentException("Invalid wavelength index", nameof(wavelengthIndex));
        }

        if (selectedTimeIndex < 0 || selectedTimeIndex >= data.NSpect)
        {
            throw new ArgumentException("Invalid time index", nameof(selectedTimeIndex));
        }

        currentDADData = data;
        currentWavelengthIndex = wavelengthIndex;
        currentSelectedTimeIndex = selectedTimeIndex;
        RedrawPlot();
    }

    private void RedrawPlot()
    {
        if (currentDADData == null)
        {
            return;
        }

        CanvasPlot.Children.Clear();

        int nSpect = currentDADData.NSpect;
        double[] intensities = new double[nSpect];
        for (int i = 0; i < nSpect; i++)
        {
            intensities[i] = currentDADData.Intensities[i, currentWavelengthIndex];
        }

        double minTime = currentDADData.TimeStamps.Min();
        double maxTime = currentDADData.TimeStamps.Max();
        double minIntensity = intensities.Min();
        double maxIntensity = intensities.Max();

        double timeRange = maxTime - minTime;
        if (timeRange == 0)
        {
            timeRange = 1;
        }

        double intensityRange = maxIntensity - minIntensity;
        if (intensityRange == 0)
        {
            intensityRange = 1;
        }

        double canvasWidth = CanvasPlot.ActualWidth;
        double canvasHeight = CanvasPlot.ActualHeight;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            canvasWidth = ActualWidth;
            canvasHeight = ActualHeight;
        }

        polyline = new Polyline
        {
            Stroke = Brushes.Blue,
            StrokeThickness = 1,
            Points = new PointCollection()
        };

        for (int i = 0; i < nSpect; i++)
        {
            double t = currentDADData.TimeStamps[i];
            double intensity = intensities[i];

            double x = ((t - minTime) / timeRange) * canvasWidth;
            double y = canvasHeight - (((intensity - minIntensity) / intensityRange) * canvasHeight);
            polyline.Points.Add(new Point(x, y));
        }

        CanvasPlot.Children.Add(polyline);

        var xAxis = new Line
        {
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            X1 = 0,
            Y1 = canvasHeight - 1,
            X2 = canvasWidth,
            Y2 = canvasHeight - 1
        };
        CanvasPlot.Children.Add(xAxis);

        var yAxis = new Line
        {
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            X1 = 0,
            Y1 = 0,
            X2 = 0,
            Y2 = canvasHeight
        };
        CanvasPlot.Children.Add(yAxis);

        var xLabelMin = new TextBlock { Text = $"{minTime:F2}", Foreground = Brushes.Black };
        Canvas.SetLeft(xLabelMin, 0);
        Canvas.SetTop(xLabelMin, canvasHeight - 20);
        CanvasPlot.Children.Add(xLabelMin);

        var xLabelMax = new TextBlock { Text = $"{maxTime:F2}", Foreground = Brushes.Black };
        Canvas.SetLeft(xLabelMax, Math.Max(0, canvasWidth - 50));
        Canvas.SetTop(xLabelMax, canvasHeight - 20);
        CanvasPlot.Children.Add(xLabelMax);

        DrawGrid(canvasWidth, canvasHeight, 5, 5);

        int selectedIndex = Math.Clamp(currentSelectedTimeIndex, 0, nSpect - 1);
        double selectedTime = currentDADData.TimeStamps[selectedIndex];
        double selectedIntensity = currentDADData.Intensities[selectedIndex, currentWavelengthIndex];

        double markerX = ((selectedTime - minTime) / timeRange) * canvasWidth;
        double markerY = canvasHeight - (((selectedIntensity - minIntensity) / intensityRange) * canvasHeight);
        ShowMarker(markerX, markerY);
    }

    private void ShowMarker(double x, double y)
    {
        if (marker != null)
        {
            CanvasPlot.Children.Remove(marker);
        }

        marker = new Ellipse
        {
            Width = 8,
            Height = 8,
            Stroke = Brushes.Red,
            StrokeThickness = 2,
            Fill = Brushes.Transparent
        };

        Canvas.SetLeft(marker, x - marker.Width / 2);
        Canvas.SetTop(marker, y - marker.Height / 2);
        CanvasPlot.Children.Add(marker);
    }

    private void DrawGrid(double canvasWidth, double canvasHeight, int numVerticalLines, int numHorizontalLines)
    {
        for (int i = 1; i < numVerticalLines; i++)
        {
            double x = i * canvasWidth / numVerticalLines;
            var line = new Line
            {
                Stroke = Brushes.LightGray,
                StrokeThickness = 0.5,
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = canvasHeight
            };
            CanvasPlot.Children.Add(line);
        }

        for (int i = 1; i < numHorizontalLines; i++)
        {
            double y = i * canvasHeight / numHorizontalLines;
            var line = new Line
            {
                Stroke = Brushes.LightGray,
                StrokeThickness = 0.5,
                X1 = 0,
                Y1 = y,
                X2 = canvasWidth,
                Y2 = y
            };
            CanvasPlot.Children.Add(line);
        }
    }

    private void CanvasPlot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (currentDADData == null)
        {
            return;
        }

        double canvasWidth = CanvasPlot.ActualWidth;
        if (canvasWidth <= 0)
        {
            return;
        }

        var pos = e.GetPosition(CanvasPlot);
        double minTime = currentDADData.TimeStamps.Min();
        double maxTime = currentDADData.TimeStamps.Max();
        double timeRange = maxTime - minTime;
        if (timeRange == 0)
        {
            return;
        }

        double clampedX = Math.Clamp(pos.X, 0, canvasWidth);
        double targetTime = minTime + (clampedX / canvasWidth) * timeRange;

        int nearestIndex = FindNearestTimeIndex(currentDADData.TimeStamps, targetTime);
        currentSelectedTimeIndex = nearestIndex;
        RedrawPlot();
        TimeIndexSelected?.Invoke(this, new TimeIndexSelectedEventArgs(nearestIndex));
    }

    private static int FindNearestTimeIndex(double[] times, double target)
    {
        int nearest = 0;
        double bestDistance = Math.Abs(times[0] - target);

        for (int i = 1; i < times.Length; i++)
        {
            double distance = Math.Abs(times[i] - target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    public class TimeIndexSelectedEventArgs(int timeIndex) : EventArgs
    {
        public int TimeIndex { get; } = timeIndex;
    }

    public event EventHandler<TimeIndexSelectedEventArgs>? TimeIndexSelected;
}
