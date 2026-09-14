# DADViewer

DADViewer is an open-source Windows desktop application for exploring chromatography data from diode-array detectors (DAD/PDA). View a measurement as an intensity map, inspect chromatograms and spectra, analyze peaks, and compare signals across supported data sources.

Built with C#, .NET 10, and WPF. Data is processed locally, and source files are opened read-only.

## Features

- Interactive intensity maps and 3D surface previews.
- Linked chromatogram and spectrum views with point selection, zoom, and pan.
- Adjustable color palettes and intensity ranges.
- Peak analysis with baseline options, peak area, height, and width measurements.
- Reference signal overlays and numerical comparison for compatible intensity units.
- CSV export for signals and analysis results, plus PNG export for maps and plots.
- Drag-and-drop loading, dataset folder selection, recent files, and restored view settings.

## Supported formats

| Source | Input | Supported data |
| --- | --- | --- |
| Agilent ChemStation / OpenLab | `.uv`, `.D` folders | UV version 31 LC and version 131 LC/OL spectra |
| Waters MassLynx | `.raw` folders, `_FUNCnnn.DAT` | PDA type-12 spectra in the six-byte encoding; companion `.IDX` and `_FUNCTNS.INF` files are required |
| Waters Empower | `.arw` | Full 3D PDA ASCII exports |
| Thermo Chromeleon | `.txt` | Full UV spectral-field ASCII exports |
| DADViewer legacy format | `.dad` | Binary time × wavelength intensity matrices |

Support applies to these specific formats. Native Chromeleon binary `.dad` and `.cmbx` files, mass spectra, single-channel imports, and changing wavelength grids are not supported. The `.dad` extension alone does not identify a compatible file.

Where the source unit is known, absorbance is normalized to mAU. Waters MassLynx currently retains its decoded raw absorbance scale, and legacy DAD retains file units. The interface and exports identify these units; incompatible scales cannot be overlaid or compared numerically.

## Build and run

Requires Windows and the .NET 10 SDK.

From the repository root:

```powershell
dotnet build DADViewer.sln -c Release
dotnet run --project DADViewer.csproj -c Release
```

Use **Open data…** to select a file, **Folder…** to open an Agilent or Waters dataset, or drag a file or dataset folder into the window. If a folder contains multiple supported channels, select the desired `.uv` or `.DAT` file directly. Keep the Waters companion files together.

Click the intensity map to inspect a point, then use **Analyze / Compare…** for peak analysis or a reference signal. Use **Export…** to save the selected view or signal.

## Portable build

Create a Windows x64 package with the included PowerShell script:

```powershell
.\scripts\Publish.ps1
```

The ZIP archive and SHA-256 checksum are written to `artifacts/releases/`. Extract the entire archive and run `DADViewer.exe`. The portable package includes the .NET runtime.

## Data and display limits

Each dataset is limited to eight million intensity values. Intensity maps and 3D surfaces are sampled previews; zoom into the map and inspect signal slices for detail. Imported measurements remain unchanged.

## License

DADViewer is licensed under the [MIT License](LICENSE). Third-party notices are listed in [ThirdPartyNotices.txt](ThirdPartyNotices.txt).
