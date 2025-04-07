# DADViewer

DADViewer is a Windows application developed using C# and WPF, designed for visualizing 2D data obtained from a Diode Array Detector (DAD). The project aims to replicate and enhance functionalities described in the initial task specification (`Task.docx`).

## Overview

Diode Array Detectors generate 2D data, typically representing multiple UV (Ultra Violet) spectra measured sequentially over time. This results in a data matrix where intensity is a function of both time and wavelength (Intensity = f(Time, Wavelength)).

This application reads DAD data from a specific binary file format and provides several ways to visualize and analyze it.

## Features

* **Load Data:** Parses and loads DAD data from the specified binary file format (containing wavelength array, time array, and the intensity matrix).
* **2D Intensity Map:** Displays the core DAD data as a 2D map where color represents the UV signal intensity.
    * **Customizable Color Scale:**
        * User can specify the number of colors in the scale.
        * Colors within the scale can be easily modified.
        * Color scale steps can be set manually by the user or determined automatically based on the data range (e.g., (Max Signal - Min Signal) / Number of Colors).
* **Chromatogram View:** Extracts and displays a chromatogram (Intensity vs. Time) for a specific wavelength. Allows interactive scrolling through wavelengths to update the chromatogram view.
* **Spectrum View:** Extracts and displays a spectrum (Intensity vs. Wavelength) for a specific time point.
* **Interactive Exploration:** Allows users to select points or slices within the data maps to view corresponding chromatograms and spectra.
  
![image](https://github.com/user-attachments/assets/d492e787-efdc-4667-8c43-10f918bd2fce)

* **3D Visualization:** Presents the DAD data as an interactive 3D surface map (Time x Wavelength vs. Intensity) that can be rotated by the user.
  
![image](https://github.com/user-attachments/assets/4f2b1cd4-00f9-4622-a6c3-e1e54420bd62)
  


## Technology

* C#
* .NET (Based on project files, likely .NET 9.0 or similar)
* WPF (Windows Presentation Foundation)

## Getting Started

1. Clone the repository.
2. Download .Net 9 SDK - https://dotnet.microsoft.com/en-us/download/dotnet/9.0
3. Open the solution in Visual Studio.
4. Build the solution to restore NuGet packages. (build restore)
5. Run the application. (dotnet build / dotnet run)
6. Use the "Load Data" command to open a DAD file and interact with the various visualizations.
