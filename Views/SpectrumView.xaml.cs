using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DADViewer.Models;

namespace DADViewer.Views
{
    public partial class SpectrumView : UserControl
    {
        private DADData currentData;
        private int currentSpectrumIndex;
        private Polyline? _polyline;
        private Ellipse? _marker;

        public SpectrumView()
        {
            InitializeComponent();
            SizeChanged += (s, e) => { RedrawPlot(); };
        }
        public void RenderSpectrum(DADData data, int eRowIndex, int spectrumIndex)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (spectrumIndex < 0 || spectrumIndex >= data.NSpect)
                throw new ArgumentException("Invalid spectrum index", nameof(spectrumIndex));

            currentData = data;
            currentSpectrumIndex = spectrumIndex;
            RedrawPlot();
        }

        private void RedrawPlot()
        {
            if (CanvasPlot == null)
            {
                System.Diagnostics.Debug.WriteLine("CanvasPlot is null.");
                return;
            }

            CanvasPlot.Children.Clear();

            if (currentData == null)
                return;

            int nWaves = currentData.NWaves;
            double[] intensities = new double[nWaves];
            for (int i = 0; i < nWaves; i++)
            {
                intensities[i] = currentData.Intensities[currentSpectrumIndex, i];
            }
            double minWave = currentData.Wavelengths.Min();
            double maxWave = currentData.Wavelengths.Max();
            double minIntensity = intensities.Min();
            double maxIntensity = intensities.Max();

            double waveRange = maxWave - minWave;
            if (waveRange == 0) waveRange = 1;
            double intensityRange = maxIntensity - minIntensity;
            if (intensityRange == 0) intensityRange = 1;
            
            double canvasWidth = CanvasPlot.ActualWidth;
            double canvasHeight = CanvasPlot.ActualHeight;
            if (canvasWidth == 0 || canvasHeight == 0)
            {
                canvasWidth = this.Width;
                canvasHeight = this.Height;
            }
            
            _polyline = new Polyline
            {
                Stroke = Brushes.Red,
                StrokeThickness = 1,
                Points = new PointCollection()
            };

            for (int i = 0; i < nWaves; i++)
            {
                double wave = currentData.Wavelengths[i];
                double intensity = intensities[i];
                
                double x = ((wave - minWave) / waveRange) * canvasWidth;
                double y = canvasHeight - (((intensity - minIntensity) / intensityRange) * canvasHeight);
                _polyline.Points.Add(new Point(x, y));
            }
            if (_polyline != null)
                CanvasPlot.Children.Add(_polyline);
            
            Line xAxis = new Line
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                X1 = 0,
                Y1 = canvasHeight - 1,
                X2 = canvasWidth,
                Y2 = canvasHeight - 1
            };
            CanvasPlot.Children.Add(xAxis);

            Line yAxis = new Line
            {
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                X1 = 0,
                Y1 = 0,
                X2 = 0,
                Y2 = canvasHeight
            };
            CanvasPlot.Children.Add(yAxis);
            
            var xLabelMin = new TextBlock { Text = $"{minWave :F2} nm", Foreground = Brushes.Black };
            Canvas.SetLeft(xLabelMin, 2);
            Canvas.SetTop(xLabelMin, canvasHeight - 20);
            CanvasPlot.Children.Add(xLabelMin);

            var xLabelMax = new TextBlock { Text = $"{maxWave:F2} nm", Foreground = Brushes.Black };
            Canvas.SetLeft(xLabelMax, canvasWidth - 50);
            Canvas.SetTop(xLabelMax, canvasHeight - 20);
            CanvasPlot.Children.Add(xLabelMax);
            
            TextBlock yLabelMin = new TextBlock { Text = $"{minIntensity:F2}", Foreground = Brushes.Black };
            Canvas.SetLeft(yLabelMin, 5);
            Canvas.SetTop(yLabelMin, canvasHeight - 40);
            CanvasPlot.Children.Add(yLabelMin);

            TextBlock yLabelMax = new TextBlock { Text = $"{maxIntensity:F2}", Foreground = Brushes.Black };
            Canvas.SetLeft(yLabelMax, 5);
            Canvas.SetTop(yLabelMax, 5);
            CanvasPlot.Children.Add(yLabelMax);
            
            DrawGrid(canvasWidth, canvasHeight, 5, 5);

            int selectedWaveIndex = nWaves / 2;
            double selectedWave = currentData.Wavelengths[selectedWaveIndex];
            double selectedIntensity = currentData.Intensities[currentSpectrumIndex, selectedWaveIndex];

            double markerX = ((selectedWave - minWave) / waveRange) * canvasWidth;
            double markerY = canvasHeight - (((selectedIntensity - minIntensity) / intensityRange) * canvasHeight);
            ShowMarker(markerX, markerY, selectedWave, selectedIntensity);
        }

        private void DrawGrid(double canvasWidth, double canvasHeight, int numVerticalLines, int numHorizontalLines)
        {
            for (int i = 1; i < numVerticalLines; i++)
            {
                double x = i * canvasWidth / numVerticalLines;
                Line vLine = new Line
                {
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    X1 = x,
                    Y1 = 0,
                    X2 = x,
                    Y2 = canvasHeight
                };
                CanvasPlot.Children.Add(vLine);
            }
            for (int i = 1; i < numHorizontalLines; i++)
            {
                double y = i * canvasHeight / numHorizontalLines;
                Line hLine = new Line
                {
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    X1 = 0,
                    Y1 = y,
                    X2 = canvasWidth,
                    Y2 = y
                };
                CanvasPlot.Children.Add(hLine);
            }
        }
        private void ShowMarker(double x, double y, double selectedWave, double selectedIntensity)
        {
            if (_marker != null)
                CanvasPlot.Children.Remove(_marker);

            _marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(_marker, x - _marker.Width / 2);
            Canvas.SetTop(_marker, y - _marker.Height / 2);
            
            _marker.ToolTip = $"Wave: {selectedWave:F2} nm\nIntensity: {selectedIntensity:F2}";
            CanvasPlot.Children.Add(_marker);
        }
    }
}
