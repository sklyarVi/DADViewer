using Microsoft.Win32;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DADViewer.Models;

namespace DADViewer.Views;

public partial class MainWindow : Window
{
    private DADData? dadData;
    private int selectedTimeIndex;
    private int selectedWavelengthIndex;
    private bool suppressSliderEvents;
    private bool suppressPeakSelectionEvents;
    private readonly List<PeakInfo> detectedPeaks = [];

    public MainWindow()
    {
        InitializeComponent();
        SettingsPanel.IsEnabled = false;
        WaveSlider.Visibility = Visibility.Hidden;
        UpdateWavelengthSelector();

        Dad2DViewControl.DataPointSelected += Dad2DViewControl_DataPointSelected;
        ChromatogramViewControl.TimeIndexSelected += ChromatogramViewControl_TimeIndexSelected;
        SpectrumViewControl.WavelengthIndexSelected += SpectrumViewControl_WavelengthIndexSelected;
        StateChanged += MainWindow_StateChanged;

        UpdateColorLegend(1, 0);
        UpdateStatusText();
        UpdateMaxRestoreButtonContent();
        PeaksDataGrid.ItemsSource = detectedPeaks;
        DAD3DViewControl.ShowGrid = Show3DGridCheckBox.IsChecked == true;
    }

    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "DAD files (*.dad)|*.dad|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        var filePath = dlg.FileName;
        try
        {
            dadData = DADData.LoadFromFile(filePath);
            SettingsPanel.IsEnabled = true;
            WaveSlider.Visibility = Visibility.Visible;

            selectedTimeIndex = dadData.NSpect / 2;
            selectedWavelengthIndex = (dadData.NumberOfWavelengths - 1) / 2;
            UpdateWavelengthSelector();

            Dad2DViewControl.NumberOfColors = (int)ColorStepsSlider.Value;
            Dad2DViewControl.CurrentColorScheme = GetSelectedColorScheme();
            Dad2DViewControl.RenderDadData(dadData);
            Dad2DViewControl.UpdateMarkerByIndices(selectedWavelengthIndex, selectedTimeIndex);

            DAD3DViewControl.ShowGrid = Show3DGridCheckBox.IsChecked == true;
            DAD3DViewControl.Render3DData(dadData);
            DisplayDataSummary(dadData);
            RenderLinkedViews();
            UpdateLegendForCurrentData();
            UpdateStatusText();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading file: {ex.Message}");
            StatusInfoText.Text = $"Error loading file: {ex.Message}";
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<Button>(source) != null)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // Ignore rare drag race with maximize/restore transition.
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Dad2DViewControl_DataPointSelected(object? sender, DAD2DView.DataPointSelectedEventArgs e)
    {
        if (dadData == null)
        {
            return;
        }

        selectedWavelengthIndex = e.ColumnIndex;
        selectedTimeIndex = e.RowIndex;
        UpdateWavelengthSelector();

        RenderLinkedViews(update2DMarker: false);
    }

    private void ChromatogramViewControl_TimeIndexSelected(object? sender, ChromatogramView.TimeIndexSelectedEventArgs e)
    {
        if (dadData == null)
        {
            return;
        }

        selectedTimeIndex = e.TimeIndex;
        RenderLinkedViews(updateChromatogram: false);
    }

    private void SpectrumViewControl_WavelengthIndexSelected(object? sender, SpectrumView.WavelengthIndexSelectedEventArgs e)
    {
        if (dadData == null)
        {
            return;
        }

        selectedWavelengthIndex = e.WavelengthIndex;
        UpdateWavelengthSelector();

        RenderLinkedViews(updateSpectrum: false);
    }

    private void DisplayDataSummary(DADData data)
    {
        double minTime = data.TimeStamps.Min();
        double maxTime = data.TimeStamps.Max();
        double minWave = data.Wavelengths.Min();
        double maxWave = data.Wavelengths.Max();

        double minIntensity = double.MaxValue;
        double maxIntensity = double.MinValue;

        for (int i = 0; i < data.NSpect; i++)
        {
            for (int j = 0; j < data.NWaves; j++)
            {
                double val = data.Intensities[i, j];
                if (val < minIntensity)
                {
                    minIntensity = val;
                }

                if (val > maxIntensity)
                {
                    maxIntensity = val;
                }
            }
        }

        MinTimeText.Text = $"Min Time: {minTime:F4}";
        MaxTimeText.Text = $"Max Time: {maxTime:F4}";
        MinWaveText.Text = $"Min Wavelength: {minWave:F1}";
        MaxWaveText.Text = $"Max Wavelength: {maxWave:F1}";
        MinIntensityText.Text = $"Min Intensity: {minIntensity:F4}";
        MaxIntensityText.Text = $"Max Intensity: {maxIntensity:F4}";
    }

    private void ColorStepsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (dadData == null)
        {
            return;
        }

        Dad2DViewControl.NumberOfColors = (int)ColorStepsSlider.Value;
        Dad2DViewControl.RenderDadData(dadData);
        Dad2DViewControl.UpdateMarkerByIndices(selectedWavelengthIndex, selectedTimeIndex);
        UpdateLegendForCurrentData();
    }

    private void WavelengthDecreaseButton_Click(object sender, RoutedEventArgs e)
    {
        ChangeWavelengthIndex(-1);
    }

    private void WavelengthIncreaseButton_Click(object sender, RoutedEventArgs e)
    {
        ChangeWavelengthIndex(1);
    }

    private void WavelengthDecrease5Button_Click(object sender, RoutedEventArgs e)
    {
        ChangeWavelengthIndex(-5);
    }

    private void WavelengthDecrease10Button_Click(object sender, RoutedEventArgs e)
    {
        ChangeWavelengthIndex(-10);
    }

    private void WavelengthIncrease5Button_Click(object sender, RoutedEventArgs e)
    {
        ChangeWavelengthIndex(5);
    }

    private void WavelengthIncrease10Button_Click(object sender, RoutedEventArgs e)
    {
        ChangeWavelengthIndex(10);
    }

    private void WavelengthIndexTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyWavelengthIndexFromText();
    }

    private void WavelengthIndexTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyWavelengthIndexFromText();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            ChangeWavelengthIndex(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            ChangeWavelengthIndex(-1);
            e.Handled = true;
        }
    }

    private void WavelengthIndexTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsWavelengthTextChangeValid(e.Text);
    }

    private void WavelengthIndexTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        string pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        if (!IsWavelengthTextChangeValid(pastedText))
        {
            e.CancelCommand();
        }
    }

    private bool IsWavelengthTextChangeValid(string incomingText)
    {
        if (incomingText.Length == 0)
        {
            return true;
        }

        if (incomingText.Any(ch => ch < '0' || ch > '9'))
        {
            return false;
        }

        string current = WavelengthIndexTextBox.Text ?? string.Empty;
        int start = WavelengthIndexTextBox.SelectionStart;
        int length = WavelengthIndexTextBox.SelectionLength;
        string proposed = current.Remove(start, length).Insert(start, incomingText);

        if (proposed.Length == 0)
        {
            return true;
        }

        if (!int.TryParse(proposed, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            return false;
        }

        int minIndex = 0;
        int maxIndex = dadData == null ? 0 : Math.Max(0, dadData.NumberOfWavelengths - 1);
        return value >= minIndex && value <= maxIndex;
    }

    private void ChangeWavelengthIndex(int delta)
    {
        if (dadData == null)
        {
            return;
        }

        int maxIndex = dadData.NumberOfWavelengths - 1;
        int newIndex = Math.Clamp(selectedWavelengthIndex + delta, 0, maxIndex);
        if (newIndex == selectedWavelengthIndex)
        {
            UpdateWavelengthSelector();
            return;
        }

        selectedWavelengthIndex = newIndex;
        UpdateWavelengthSelector();
        RenderLinkedViews();
    }

    private void ApplyWavelengthIndexFromText()
    {
        if (dadData == null || suppressSliderEvents)
        {
            return;
        }

        bool parsed = int.TryParse(
            WavelengthIndexTextBox.Text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int enteredIndex);

        if (!parsed)
        {
            UpdateWavelengthSelector();
            return;
        }

        int maxIndex = dadData.NumberOfWavelengths - 1;
        int newIndex = Math.Clamp(enteredIndex, 0, maxIndex);
        if (newIndex == selectedWavelengthIndex)
        {
            UpdateWavelengthSelector();
            return;
        }

        selectedWavelengthIndex = newIndex;
        UpdateWavelengthSelector();
        RenderLinkedViews();
    }

    private void UpdateWavelengthSelector()
    {
        int maxIndex = dadData == null ? 0 : Math.Max(0, dadData.NumberOfWavelengths - 1);
        int clampedIndex = Math.Clamp(selectedWavelengthIndex, 0, maxIndex);
        selectedWavelengthIndex = clampedIndex;

        suppressSliderEvents = true;
        WavelengthIndexTextBox.MaxLength = Math.Max(1, maxIndex.ToString(CultureInfo.InvariantCulture).Length);
        WavelengthIndexTextBox.Text = clampedIndex.ToString(CultureInfo.InvariantCulture);
        WavelengthRangeText.Text = $"/ {maxIndex}";
        bool canDecrease = clampedIndex > 0 && dadData != null;
        bool canIncrease = clampedIndex < maxIndex && dadData != null;
        WavelengthDecreaseButton.IsEnabled = canDecrease;
        WavelengthDecrease5Button.IsEnabled = canDecrease;
        WavelengthDecrease10Button.IsEnabled = canDecrease;
        WavelengthIncreaseButton.IsEnabled = canIncrease;
        WavelengthIncrease5Button.IsEnabled = canIncrease;
        WavelengthIncrease10Button.IsEnabled = canIncrease;
        suppressSliderEvents = false;
    }

    private void Show3DGridCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        DAD3DViewControl.ShowGrid = Show3DGridCheckBox.IsChecked == true;
    }

    private void ColorSchemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dadData == null)
        {
            return;
        }

        Dad2DViewControl.CurrentColorScheme = GetSelectedColorScheme();
        Dad2DViewControl.RenderDadData(dadData);
        Dad2DViewControl.UpdateMarkerByIndices(selectedWavelengthIndex, selectedTimeIndex);
        UpdateLegendForCurrentData();
    }

    private void Export2DImage_Click(object sender, RoutedEventArgs e)
    {
        if (dadData == null)
        {
            MessageBox.Show("Load DAD data first.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = "dad_2d.png"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Dad2DViewControl.ExportCurrentImageAsPng(dlg.FileName);
            MessageBox.Show("2D image exported successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}");
        }
    }

    private void ExportChromatogramCsv_Click(object sender, RoutedEventArgs e)
    {
        if (dadData == null)
        {
            MessageBox.Show("Load DAD data first.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"chromatogram_wave_{selectedWavelengthIndex}.csv"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(dlg.FileName);
            writer.WriteLine("time,intensity,wavelength_nm,wavelength_index,selected_time_index");
            for (int i = 0; i < dadData.NSpect; i++)
            {
                writer.WriteLine(
                    $"{dadData.TimeStamps[i].ToString(CultureInfo.InvariantCulture)}," +
                    $"{dadData.Intensities[i, selectedWavelengthIndex].ToString(CultureInfo.InvariantCulture)}," +
                    $"{dadData.Wavelengths[selectedWavelengthIndex].ToString(CultureInfo.InvariantCulture)}," +
                    $"{selectedWavelengthIndex}," +
                    $"{selectedTimeIndex}");
            }

            MessageBox.Show("Chromatogram CSV exported successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}");
        }
    }

    private void ExportSpectrumCsv_Click(object sender, RoutedEventArgs e)
    {
        if (dadData == null)
        {
            MessageBox.Show("Load DAD data first.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"spectrum_time_{selectedTimeIndex}.csv"
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var writer = new StreamWriter(dlg.FileName);
            writer.WriteLine("wavelength_nm,intensity,time,time_index,selected_wavelength_index");
            for (int i = 0; i < dadData.NWaves; i++)
            {
                writer.WriteLine(
                    $"{dadData.Wavelengths[i].ToString(CultureInfo.InvariantCulture)}," +
                    $"{dadData.Intensities[selectedTimeIndex, i].ToString(CultureInfo.InvariantCulture)}," +
                    $"{dadData.TimeStamps[selectedTimeIndex].ToString(CultureInfo.InvariantCulture)}," +
                    $"{selectedTimeIndex}," +
                    $"{selectedWavelengthIndex}");
            }

            MessageBox.Show("Spectrum CSV exported successfully.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}");
        }
    }

    private void RenderLinkedViews(bool update2DMarker = true, bool updateChromatogram = true, bool updateSpectrum = true)
    {
        if (dadData == null)
        {
            return;
        }

        selectedTimeIndex = Math.Clamp(selectedTimeIndex, 0, dadData.NSpect - 1);
        selectedWavelengthIndex = Math.Clamp(selectedWavelengthIndex, 0, dadData.NWaves - 1);
        UpdateWavelengthSelector();

        if (updateChromatogram)
        {
            ChromatogramViewControl.RenderChromatogram(dadData, selectedWavelengthIndex, selectedTimeIndex);
        }

        if (updateSpectrum)
        {
            SpectrumViewControl.RenderSpectrum(dadData, selectedTimeIndex, selectedWavelengthIndex);
        }

        if (update2DMarker)
        {
            Dad2DViewControl.UpdateMarkerByIndices(selectedWavelengthIndex, selectedTimeIndex);
        }

        UpdateDetectedPeaks();
        UpdateStatusText();
    }

    private void PeaksDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressPeakSelectionEvents || dadData == null)
        {
            return;
        }

        if (PeaksDataGrid.SelectedItem is not PeakInfo peak)
        {
            return;
        }

        selectedTimeIndex = Math.Clamp(peak.TimeIndex, 0, dadData.NSpect - 1);
        RenderLinkedViews();
    }

    private string GetSelectedColorScheme()
    {
        var selectedItem = ColorSchemeComboBox.SelectedItem as ComboBoxItem;
        return selectedItem?.Content.ToString() ?? "Blue-Red";
    }

    private void UpdateLegendForCurrentData()
    {
        if (dadData == null)
        {
            return;
        }

        double minIntensity = double.MaxValue;
        double maxIntensity = double.MinValue;

        for (int i = 0; i < dadData.NSpect; i++)
        {
            for (int j = 0; j < dadData.NWaves; j++)
            {
                double value = dadData.Intensities[i, j];
                if (value < minIntensity)
                {
                    minIntensity = value;
                }

                if (value > maxIntensity)
                {
                    maxIntensity = value;
                }
            }
        }

        UpdateColorLegend(maxIntensity, minIntensity);
    }

    private void UpdateColorLegend(double maxIntensity, double minIntensity)
    {
        const int width = 20;
        const int height = 200;

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        int[] pixels = new int[width * height];

        for (int y = 0; y < height; y++)
        {
            double norm = 1.0 - (double)y / (height - 1);
            if (Dad2DViewControl.NumberOfColors > 1)
            {
                double stepSize = 1.0 / (Dad2DViewControl.NumberOfColors - 1);
                norm = Math.Round(norm / stepSize) * stepSize;
                norm = Math.Clamp(norm, 0, 1);
            }

            Color color = Helpers.ColorMapHelper.GetColor(norm, Dad2DViewControl.NumberOfColors, Dad2DViewControl.CurrentColorScheme);
            int packed = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;

            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = packed;
            }
        }

        bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        ColorLegendImage.Source = bitmap;
        LegendMaxValueText.Text = maxIntensity.ToString("F2", CultureInfo.InvariantCulture);
        LegendMinValueText.Text = minIntensity.ToString("F2", CultureInfo.InvariantCulture);
    }

    private void UpdateStatusText()
    {
        if (dadData == null)
        {
            StatusInfoText.Text = "No data loaded.";
            return;
        }

        int clampedTime = Math.Clamp(selectedTimeIndex, 0, dadData.NSpect - 1);
        int clampedWave = Math.Clamp(selectedWavelengthIndex, 0, dadData.NWaves - 1);

        double time = dadData.TimeStamps[clampedTime];
        double wave = dadData.Wavelengths[clampedWave];
        double intensity = dadData.Intensities[clampedTime, clampedWave];

        StatusInfoText.Text =
            $"Selected: t#{clampedTime} ({time:F3}), w#{clampedWave} ({wave:F1} nm), I={intensity:F4}  " +
            $"| Matrix: {dadData.NSpect}x{dadData.NWaves}  " +
            $"| 2D render: {Dad2DViewControl.RenderedWidth}x{Dad2DViewControl.RenderedHeight}";
    }

    private void UpdateDetectedPeaks()
    {
        if (dadData == null)
        {
            detectedPeaks.Clear();
            PeaksDataGrid.Items.Refresh();
            PeaksHeaderText.Text = "Detected peaks (current wavelength)";
            return;
        }

        selectedWavelengthIndex = Math.Clamp(selectedWavelengthIndex, 0, dadData.NWaves - 1);

        List<PeakInfo> peaks = DetectPeaksForWavelength(dadData, selectedWavelengthIndex, 24);
        detectedPeaks.Clear();
        detectedPeaks.AddRange(peaks);

        suppressPeakSelectionEvents = true;
        PeaksDataGrid.Items.Refresh();
        PeaksHeaderText.Text = $"Detected peaks ({dadData.Wavelengths[selectedWavelengthIndex]:F1} nm)";

        PeakInfo? closest = detectedPeaks
            .OrderBy(p => Math.Abs(p.TimeIndex - selectedTimeIndex))
            .FirstOrDefault();
        PeaksDataGrid.SelectedItem = closest;
        if (closest != null)
        {
            PeaksDataGrid.ScrollIntoView(closest);
        }
        suppressPeakSelectionEvents = false;
    }

    private static List<PeakInfo> DetectPeaksForWavelength(DADData data, int wavelengthIndex, int maxPeaks)
    {
        int n = data.NSpect;
        if (n < 3)
        {
            return [];
        }

        var signal = new double[n];
        for (int i = 0; i < n; i++)
        {
            signal[i] = data.Intensities[i, wavelengthIndex];
        }

        double[] smoothed = SmoothSignal(signal, windowSize: 7);
        double min = smoothed.Min();
        double max = smoothed.Max();
        double range = max - min;
        if (range <= 0)
        {
            return [];
        }

        double minHeight = min + range * 0.07;
        double minProminence = range * 0.03;
        int minDistance = Math.Max(3, n / 180);

        var candidates = new List<(int index, double intensity, double prominence)>();
        for (int i = 1; i < n - 1; i++)
        {
            if (smoothed[i] <= smoothed[i - 1] || smoothed[i] < smoothed[i + 1])
            {
                continue;
            }

            double intensity = signal[i];
            if (intensity < minHeight)
            {
                continue;
            }

            double prominence = EstimateProminence(smoothed, i);
            if (prominence < minProminence)
            {
                continue;
            }

            candidates.Add((i, intensity, prominence));
        }

        var selected = new List<(int index, double intensity, double prominence)>();
        foreach (var candidate in candidates.OrderByDescending(c => c.intensity))
        {
            bool tooClose = selected.Any(p => Math.Abs(p.index - candidate.index) < minDistance);
            if (tooClose)
            {
                continue;
            }

            selected.Add(candidate);
            if (selected.Count >= maxPeaks)
            {
                break;
            }
        }

        return selected
            .OrderBy(p => p.index)
            .Select((p, i) => new PeakInfo(
                i + 1,
                p.index,
                data.TimeStamps[p.index],
                p.intensity,
                p.prominence))
            .ToList();
    }

    private static double[] SmoothSignal(double[] values, int windowSize)
    {
        int n = values.Length;
        if (n == 0)
        {
            return [];
        }

        int half = Math.Max(1, windowSize / 2);
        var smoothed = new double[n];
        for (int i = 0; i < n; i++)
        {
            int start = Math.Max(0, i - half);
            int end = Math.Min(n - 1, i + half);

            double sum = 0;
            int count = 0;
            for (int j = start; j <= end; j++)
            {
                sum += values[j];
                count++;
            }

            smoothed[i] = sum / count;
        }

        return smoothed;
    }

    private static double EstimateProminence(double[] values, int peakIndex)
    {
        int n = values.Length;
        int span = Math.Max(8, n / 120);

        int leftStart = Math.Max(0, peakIndex - span);
        int rightEnd = Math.Min(n - 1, peakIndex + span);

        double leftMin = values[leftStart];
        for (int i = leftStart; i <= peakIndex; i++)
        {
            if (values[i] < leftMin)
            {
                leftMin = values[i];
            }
        }

        double rightMin = values[peakIndex];
        for (int i = peakIndex; i <= rightEnd; i++)
        {
            if (values[i] < rightMin)
            {
                rightMin = values[i];
            }
        }

        double localBaseline = Math.Max(leftMin, rightMin);
        return values[peakIndex] - localBaseline;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        UpdateMaxRestoreButtonContent();
    }

    private void ToggleMaximizeRestore()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        UpdateMaxRestoreButtonContent();
    }

    private void UpdateMaxRestoreButtonContent()
    {
        if (MaxRestoreButton == null)
        {
            return;
        }

        MaxRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class PeakInfo(int index, int timeIndex, double timeValue, double intensityValue, double prominenceValue)
    {
        public int TimeIndex { get; } = timeIndex;
        public string Index { get; } = index.ToString(CultureInfo.InvariantCulture);
        public string Time { get; } = timeValue.ToString("F3", CultureInfo.InvariantCulture);
        public string Intensity { get; } = intensityValue.ToString("F4", CultureInfo.InvariantCulture);
        public string Prominence { get; } = prominenceValue.ToString("F4", CultureInfo.InvariantCulture);
    }
}
