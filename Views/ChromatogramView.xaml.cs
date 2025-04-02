using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using DADViewer.Models;

namespace DADViewer.Views
{
    public partial class ChromatogramView : UserControl
    {
        private DADData currentDADData;
        private int currentWavelengthIndex;
        private Polyline? _polyline;
        private Ellipse? _marker;
        private int currentSelectedTimeIndex = -1;


        public ChromatogramView()
        {
            InitializeComponent();
            // Redraw plot when control size changes (to update scaling and axes)
            this.SizeChanged += (s, e) => { RedrawPlot(); };
        }

        /// <summary>
        /// Renderuje chromatogram dla podanego zestawu danych i wybranego indeksu długości fali.
        /// </summary>
        /// <param name="data">Dane DAD</param>
        /// <param name="wavelengthIndex">Indeks długości fali</param>
        /// <param name="selectedTimeIndex"></param>
        public void RenderChromatogram(DADData data, int wavelengthIndex, int selectedTimeIndex)
        {
            if (wavelengthIndex < 0 || wavelengthIndex >= data.NWaves)
                throw new ArgumentException("Invalid wavelength index");

            if (selectedTimeIndex < 0 || selectedTimeIndex >= data.NSpect)
                throw new ArgumentException("Invalid time index");

            currentDADData = data;
            currentWavelengthIndex = wavelengthIndex;
            currentSelectedTimeIndex = selectedTimeIndex;

            RedrawPlot();
        }

        /// <summary>
        /// Przerysowuje wykres chromatogramu na podstawie aktualnych danych i wybranego indeksu.
        /// </summary>
        private void RedrawPlot()
        {
            if (currentDADData == null)
                return;

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
            if (timeRange == 0) timeRange = 1;
            double intensityRange = maxIntensity - minIntensity;
            if (intensityRange == 0) intensityRange = 1;

            // Pobierz wymiary Canvas
            double canvasWidth = CanvasPlot.ActualWidth;
            double canvasHeight = CanvasPlot.ActualHeight;
            if (canvasWidth == 0 || canvasHeight == 0)
            {
                // Jeśli ActualWidth/Height nie są dostępne, użyj wymiarów kontrolki
                canvasWidth = this.Width;
                canvasHeight = this.Height;
            }

            // Utwórz Polyline przedstawiający wykres chromatogramu
            _polyline = new Polyline
            {
                Stroke = Brushes.Blue,
                StrokeThickness = 1,
                Points = new PointCollection()
            };

            for (int i = 0; i < nSpect; i++)
            {
                double t = currentDADData.TimeStamps[i];
                double intensity = intensities[i];

                // Mapowanie wartości czasu na współrzędną X
                double x = ((t - minTime) / timeRange) * canvasWidth;
                // Mapowanie intensywności na współrzędną Y (odwrócone, bo y=0 na górze)
                double y = canvasHeight - (((intensity - minIntensity) / intensityRange) * canvasHeight);
                _polyline.Points.Add(new Point(x, y));
            }
            CanvasPlot.Children.Add(_polyline);

            // Rysowanie osi: oś X i oś Y
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
            Canvas.SetLeft(xLabelMax, canvasWidth - 50);
            Canvas.SetTop(xLabelMax, canvasHeight - 20);
            CanvasPlot.Children.Add(xLabelMax);
            
            DrawGrid(canvasWidth, canvasHeight, 5, 5, minTime, maxTime, minIntensity, maxIntensity);

            
            // Dodaj marker – przykładowo ustawiony na środkowy punkt wykresu
            int selectedIndex = currentSelectedTimeIndex; // Możesz zastąpić tym indeksem wybranym przez użytkownika
            double selectedTime = currentDADData.TimeStamps[selectedIndex];
            double selectedIntensity = currentDADData.Intensities[selectedIndex, currentWavelengthIndex];

            double markerX = ((selectedTime - minTime) / timeRange) * canvasWidth;
            double markerY = canvasHeight - (((selectedIntensity - minIntensity) / intensityRange) * canvasHeight);
            ShowMarker(markerX, markerY);
        }

        /// <summary>
        /// Dodaje lub aktualizuje marker (czerwoną elipsę) w określonym punkcie wykresu.
        /// </summary>
        /// <param name="x">Współrzędna X na Canvas</param>
        /// <param name="y">Współrzędna Y na Canvas</param>
        private void ShowMarker(double x, double y)
        {
            if (_marker != null)
            {
                CanvasPlot.Children.Remove(_marker);
            }
            _marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = Brushes.Transparent
            };
            // Centruj marker względem punktu (x, y)
            Canvas.SetLeft(_marker, x - _marker.Width / 2);
            Canvas.SetTop(_marker, y - _marker.Height / 2);
            CanvasPlot.Children.Add(_marker);
        }
        
        private void DrawGrid(double canvasWidth, double canvasHeight, int numVerticalLines, int numHorizontalLines, double minTime, double maxTime, double minIntensity, double maxIntensity)
        {
            // Pionowe linie (dla osi czasu)
            for (int i = 1; i < numVerticalLines; i++)
            {
                double x = i * canvasWidth / numVerticalLines;
                Line line = new Line
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
            // Poziome linie (dla intensywności)
            for (int i = 1; i < numHorizontalLines; i++)
            {
                double y = i * canvasHeight / numHorizontalLines;
                Line line = new Line
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
    }
}
