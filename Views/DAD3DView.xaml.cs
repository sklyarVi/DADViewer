using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DADViewer.Models;
using HelixToolkit.Wpf;

namespace DADViewer.Views;

public partial class DAD3DView : UserControl
{
    public DAD3DView()
    {
        InitializeComponent();
    }
    
    public void Render3DData(DADData dadData)
    {
        var meshBuilder = new MeshBuilder(false, false);
        
        // Example of scaling
        double xScale = 1;
        double yScale = 1;
        double zScale = 1;

        int nSpect = dadData.NSpect;
        int nWaves = dadData.NWaves;
        
        double minWave = double.MaxValue, maxWave = double.MinValue;
        foreach (var w in dadData.Wavelengths)
        {
            if (w < minWave) minWave = w;
            if (w > maxWave) maxWave = w;
        }

        double minTime = double.MaxValue, maxTime = double.MinValue;
        foreach (var t in dadData.TimeStamps)
        {
            if (t < minTime) minTime = t;
            if (t > maxTime) maxTime = t;
        }

        double minIntensity = double.MaxValue, maxIntensity = double.MinValue;
        for (int i = 0; i < nSpect; i++)
        {
            for (int j = 0; j < nWaves; j++)
            {
                double val = dadData.Intensities[i, j];
                if (val < minIntensity) minIntensity = val;
                if (val > maxIntensity) maxIntensity = val;
            }
        }

        // Obliczenie środka sceny
        double centerX = ((minWave + maxWave) / 2) * xScale;
        double centerY = ((minTime + maxTime) / 2) * yScale;
        double centerZ = ((minIntensity + maxIntensity) / 2) * zScale;
        
        // We create a surface grid based on the intensity data.
        // In this example: axis X = wavelength index, Y = spectral index, Z = intensity
        for (int i = 0; i < nSpect - 1; i++)
        {
            for (int j = 0; j < nWaves - 1; j++)
            {
                // Get the actual values for time and wavelength:
                double time0 = dadData.TimeStamps[i];
                double time1 = dadData.TimeStamps[i + 1];
                double wave0 = dadData.Wavelengths[j];
                double wave1 = dadData.Wavelengths[j + 1];
                
                // 4 points forming a quadrilateral of the surface
                var p00 = new Point3D(wave0 * xScale, time0 * yScale, dadData.Intensities[i, j] * zScale);
                var p10 = new Point3D(wave1 * xScale, time0 * yScale, dadData.Intensities[i, j + 1] * zScale);
                var p11 = new Point3D(wave1 * xScale, time1 * yScale, dadData.Intensities[i + 1, j + 1] * zScale);
                var p01 = new Point3D(wave0 * xScale, time1 * yScale, dadData.Intensities[i + 1, j] * zScale);
                
                meshBuilder.AddQuad(p00, p10, p11, p01);
            }
        }
        var mesh = meshBuilder.ToMesh(true);
        var material = MaterialHelper.CreateMaterial(new SolidColorBrush(Colors.LightGreen),
            specularPower: 100D,
            ambient: 77,
            freeze: true);
        var model = new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material
        };
            
        HelixViewport.Children.Clear();
        HelixViewport.Children.Add(new DefaultLights());
        HelixViewport.Children.Add(new ModelVisual3D { Content = model });
            
        // Ustawienie kamery – wyznaczamy pozycję kamery na podstawie środka sceny
        HelixViewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(centerX, centerY - 100, centerZ + 100),
            LookDirection = new Vector3D(0, 100, -100),
            UpDirection = new Vector3D(0, 0, 1),
            FieldOfView = 60
        };
            
        // Automatycznie dopasowuje widok do zawartości sceny
        HelixViewport.ZoomExtents();
    }

}