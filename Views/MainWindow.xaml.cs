using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using DADViewer.Models;

namespace DADViewer.Views;

public partial class MainWindow : Window
{
    private DADData dadData;
    private bool isDataLoaded = false;

    public MainWindow()
    {
        InitializeComponent();
        SettingsPanel.IsEnabled = false;
        WaveSlider.Visibility = Visibility.Hidden;
        if (dadData == null) return;
        DAD3DViewControl.Render3DData(dadData);
        DisplayDataSummary(dadData);
    }
    
    private void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "DAD files (*.dad)|*.dad|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        var filePath = dlg.FileName;
        try
        {
            dadData = DADData.LoadFromFile(filePath);
            isDataLoaded = true;
            SettingsPanel.IsEnabled = true;
            WaveSlider.Visibility = Visibility.Visible;
            Dad2DViewControl.RenderDadData(dadData);
            DAD3DViewControl.Render3DData(dadData);
            DisplayDataSummary(dadData);
            SetWavelengthSliderLimits();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading file: {ex.Message}");
        }
        Dad2DViewControl.DataPointSelected += Dad2DViewControl_DataPointSelected;
    }
    
    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void Dad2DViewControl_DataPointSelected(object? sender, DAD2DView.DataPointSelectedEventArgs e)
    {
        // Update the ChromatogramView for the selected wavelength index (column).
        ChromatogramViewControl.RenderChromatogram(dadData, e.ColumnIndex, e.RowIndex);
        // Update SpectrumView for the selected spectrum (line)
        SpectrumViewControl.RenderSpectrum(dadData, e.RowIndex, e.ColumnIndex);
        WavelengthSlider.Value = e.ColumnIndex;
    }

    private void DisplayDataSummary(DADData dadData)
    {
        if (dadData == null)
        {
            MessageBox.Show("No data available");
            return;
        }

        double minTime = dadData.TimeStamps.Min();
        double maxTime = dadData.TimeStamps.Max();
        double minWave = dadData.Wavelengths.Min();
        double maxWave = dadData.Wavelengths.Max();

        double minIntensity = double.MaxValue, maxIntensity = double.MinValue;
        for (int i = 0; i < dadData.NSpect; i++)
        {
            for (int j = 0; j < dadData.NWaves; j++)
            {
                double val = dadData.Intensities[i, j];
                if (val < minIntensity) minIntensity = val;
                if (val > maxIntensity) maxIntensity = val;
            }
        }

        MinTimeText.Text = $"Min Time: {minTime}";
        MaxTimeText.Text = $"Max Time: {maxTime}";
        MinWaveText.Text = $"Min Wavelength: {minWave}";
        MaxWaveText.Text = $"Max Wavelength: {maxWave}";
        MinIntensityText.Text = $"Min Intensity: {minIntensity}";
        MaxIntensityText.Text = $"Max Intensity: {maxIntensity}";
    }

    private void ColorStepsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (Dad2DViewControl == null) return;
        Dad2DViewControl.NumberOfColors = (int)ColorStepsSlider.Value;
        Dad2DViewControl.RenderDadData(dadData);
    }
    
    private void WavelengthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (dadData != null)
        {
            int waveIndex = (int)WavelengthSlider.Value;
            WavelengthValueText.Text = waveIndex.ToString();
            int selectedTimeIndex = dadData.NSpect / 2;
            ChromatogramViewControl.RenderChromatogram(dadData, waveIndex, selectedTimeIndex);
            Dad2DViewControl.UpdateMarkerByWavelength(waveIndex);
        }
    }
    
    private void SetWavelengthSliderLimits()
    {
        if (dadData != null)
        {
            WavelengthSlider.Maximum = dadData.NumberOfWavelengths - 1;
            WavelengthSlider.Value = (dadData.NumberOfWavelengths - 1) / 2;
        }
    }
    
    private void ColorSchemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Dad2DViewControl != null && dadData != null)
        {
            var selectedItem = ColorSchemeComboBox.SelectedItem as ComboBoxItem;
            string scheme = selectedItem?.Content.ToString() ?? "Blue-Red";
            
            Dad2DViewControl.CurrentColorScheme = scheme;
            Dad2DViewControl.RenderDadData(dadData);
        }
    }
    
}