# DADViewer

**DADViewer** is a WPF application developed in C# for visualizing data from a Diode Array Detector (DAD). It reads binary DAD files—which contain multiple UV spectra measured over time—and displays the data in several interactive views.

## Features

- **2D Heatmap**  
  Visualizes intensity values as a heatmap with a configurable color scale.  
  - Change the number of colors in the gradient.
  - Select from pre-defined color palettes (e.g., Blue-Red, Viridis, Jet, Hot).

- **Chromatogram**  
  Plots intensity versus time for a selected wavelength.  
  - Interactive updates when selecting a point on the 2D heatmap.
  - Displays selected wavelength and intensity values.

- **Spectrum**  
  Plots intensity versus wavelength for a selected time point.  
  - Interactive updates when selecting a point on the 2D heatmap.
  - Shows selected time and intensity values.


![image](https://github.com/user-attachments/assets/d492e787-efdc-4667-8c43-10f918bd2fce)


- **3D Surface Plot**  
  Displays a rotatable 3D map of the data (time, wavelength, and intensity).  
  - Supports user interaction (rotation, zoom, and pan).
  - Colors the surface based on intensity values.
    

![image](https://github.com/user-attachments/assets/4f2b1cd4-00f9-4622-a6c3-e1e54420bd62)


## How It Works

1. **Loading Data**  
   Use the "Load Data" command (accessible from the menu or toolbar) to open a DAD file. The application reads the file asynchronously and creates a `DADData` model containing:
   - Number of wavelengths (`nWaves`)
   - Number of spectra (`nSpect`)
   - An array of time values (for each spectrum)
   - An array of wavelengths
   - A 2D array of intensity values

2. **Interactive Visualization**  
   - The **2D Heatmap** displays the intensity data with a customizable color scale.  
   - Clicking on the heatmap updates the **Chromatogram** (intensity vs. time for the selected wavelength) and **Spectrum** (intensity vs. wavelength for the selected time point).  
   - A slider allows scrolling through wavelengths interactively, updating the chromatogram accordingly.
   - The **3D Surface Plot** shows a rotatable view of the data with the surface colored by intensity.

## Requirements

- .NET 9.0 (Windows)
- WPF
- HelixToolkit.Wpf
- Microsoft.Extensions.DependencyInjection

## Getting Started

1. Clone the repository.
2. Open the solution in Visual Studio.
3. Build the solution to restore NuGet packages.
4. Run the application.
5. Use the "Load Data" command to open a DAD file and interact with the various visualizations.



