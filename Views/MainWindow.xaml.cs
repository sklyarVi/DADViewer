using Microsoft.Win32;
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

    public MainWindow()
    {
        InitializeComponent();
        SettingsPanel.IsEnabled = false;
        WaveSlider.Visibility = Visibility.Hidden;

        Dad2DViewControl.DataPointSelected += Dad2DViewControl_DataPointSelected;
        ChromatogramViewControl.TimeIndexSelected += ChromatogramViewControl_TimeIndexSelected;
        SpectrumViewControl.WavelengthIndexSelected += SpectrumViewControl_WavelengthIndexSelected;
        StateChanged += MainWindow_StateChanged;

        UpdateColorLegend(1, 0);
        UpdateStatusText();
        UpdateMaxRestoreButtonContent();
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
            SetWavelengthSliderLimits();
            selectedWavelengthIndex = (int)WavelengthSlider.Value;

            Dad2DViewControl.NumberOfColors = (int)ColorStepsSlider.Value;
            Dad2DViewControl.CurrentColorScheme = GetSelectedColorScheme();
            Dad2DViewControl.RenderDadData(dadData);
            Dad2DViewControl.UpdateMarkerByIndices(selectedWavelengthIndex, selectedTimeIndex);

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

        suppressSliderEvents = true;
        WavelengthSlider.Value = selectedWavelengthIndex;
        WavelengthValueText.Text = selectedWavelengthIndex.ToString(CultureInfo.InvariantCulture);
        suppressSliderEvents = false;

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

        suppressSliderEvents = true;
        WavelengthSlider.Value = selectedWavelengthIndex;
        WavelengthValueText.Text = selectedWavelengthIndex.ToString(CultureInfo.InvariantCulture);
        suppressSliderEvents = false;

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

    private void WavelengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (dadData == null)
        {
            return;
        }

        selectedWavelengthIndex = Math.Clamp((int)WavelengthSlider.Value, 0, dadData.NumberOfWavelengths - 1);
        WavelengthValueText.Text = selectedWavelengthIndex.ToString(CultureInfo.InvariantCulture);

        if (suppressSliderEvents)
        {
            return;
        }

        RenderLinkedViews();
    }

    private void SetWavelengthSliderLimits()
    {
        if (dadData == null)
        {
            return;
        }

        WavelengthSlider.Maximum = dadData.NumberOfWavelengths - 1;
        WavelengthSlider.Value = (dadData.NumberOfWavelengths - 1) / 2;
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

        UpdateStatusText();
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
}
