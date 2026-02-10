using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DADViewer.Models;

namespace DADViewer.Views;

public partial class DAD3DView : UserControl
{
    private readonly Model3DGroup sceneGroup = new();
    private readonly ModelVisual3D sceneVisual = new();
    private readonly PerspectiveCamera camera = new();

    private Point? lastMousePosition;
    private Point3D sceneCenter = new(50, 50, 50);
    private double cameraDistance = 250;
    private double yawDegrees = 45;
    private double pitchDegrees = -35;

    public DAD3DView()
    {
        InitializeComponent();

        sceneVisual.Content = sceneGroup;
        DataViewport.Children.Add(sceneVisual);

        camera.FieldOfView = 60;
        DataViewport.Camera = camera;
        UpdateCamera();
    }

    public int MaxSpectraSamples { get; set; } = 250;
    public int MaxWavelengthSamples { get; set; } = 250;

    public void Render3DData(DADData dadData)
    {
        if (dadData.NSpect < 2 || dadData.NWaves < 2)
        {
            sceneGroup.Children.Clear();
            return;
        }

        int[] sampledRows = BuildSampleIndexArray(dadData.NSpect, MaxSpectraSamples);
        int[] sampledColumns = BuildSampleIndexArray(dadData.NWaves, MaxWavelengthSamples);

        double minWave = dadData.Wavelengths.Min();
        double maxWave = dadData.Wavelengths.Max();
        double minTime = dadData.TimeStamps.Min();
        double maxTime = dadData.TimeStamps.Max();

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

        double waveRange = maxWave - minWave;
        double timeRange = maxTime - minTime;
        double intensityRange = maxIntensity - minIntensity;

        if (waveRange == 0)
        {
            waveRange = 1;
        }

        if (timeRange == 0)
        {
            timeRange = 1;
        }

        if (intensityRange == 0)
        {
            intensityRange = 1;
        }

        const double xScale = 100;
        const double yScale = 100;
        const double zScale = 100;

        int rowCount = sampledRows.Length;
        int colCount = sampledColumns.Length;

        var positions = new Point3DCollection(rowCount * colCount);
        var textureCoordinates = new PointCollection(rowCount * colCount);
        for (int r = 0; r < rowCount; r++)
        {
            int sourceRow = sampledRows[r];
            double y = ((dadData.TimeStamps[sourceRow] - minTime) / timeRange) * yScale;

            for (int c = 0; c < colCount; c++)
            {
                int sourceColumn = sampledColumns[c];
                double x = ((dadData.Wavelengths[sourceColumn] - minWave) / waveRange) * xScale;
                double z = ((dadData.Intensities[sourceRow, sourceColumn] - minIntensity) / intensityRange) * zScale;

                positions.Add(new Point3D(x, y, z));
                textureCoordinates.Add(new Point((double)c / (colCount - 1), (double)r / (rowCount - 1)));
            }
        }

        var triangleIndices = new Int32Collection((rowCount - 1) * (colCount - 1) * 6);
        for (int r = 0; r < rowCount - 1; r++)
        {
            for (int c = 0; c < colCount - 1; c++)
            {
                int p00 = r * colCount + c;
                int p10 = p00 + 1;
                int p01 = (r + 1) * colCount + c;
                int p11 = p01 + 1;

                triangleIndices.Add(p00);
                triangleIndices.Add(p01);
                triangleIndices.Add(p10);

                triangleIndices.Add(p10);
                triangleIndices.Add(p01);
                triangleIndices.Add(p11);
            }
        }

        var mesh = new MeshGeometry3D
        {
            Positions = positions,
            TriangleIndices = triangleIndices,
            TextureCoordinates = textureCoordinates
        };

        var gradient = new LinearGradientBrush();
        gradient.StartPoint = new Point(0, 1);
        gradient.EndPoint = new Point(0, 0);
        gradient.GradientStops.Add(new GradientStop(Colors.SteelBlue, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Gold, 1));

        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(gradient));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 50));

        var model = new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material
        };

        sceneGroup.Children.Clear();
        sceneGroup.Children.Add(new AmbientLight(Colors.Gray));
        sceneGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, -0.3, -1)));
        sceneGroup.Children.Add(model);

        Rect3D bounds = mesh.Bounds;
        sceneCenter = new Point3D(
            bounds.X + bounds.SizeX / 2,
            bounds.Y + bounds.SizeY / 2,
            bounds.Z + bounds.SizeZ / 2);

        double maxSize = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
        cameraDistance = Math.Max(120, maxSize * 2.2);
        yawDegrees = 45;
        pitchDegrees = -35;
        UpdateCamera();
    }

    private void DataViewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        lastMousePosition = e.GetPosition(this);
        DataViewport.CaptureMouse();
    }

    private void DataViewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        lastMousePosition = null;
        DataViewport.ReleaseMouseCapture();
    }

    private void DataViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (lastMousePosition == null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point current = e.GetPosition(this);
        Vector delta = current - lastMousePosition.Value;
        lastMousePosition = current;

        yawDegrees += delta.X * 0.5;
        pitchDegrees -= delta.Y * 0.5;
        pitchDegrees = Math.Clamp(pitchDegrees, -89, 89);

        UpdateCamera();
    }

    private void DataViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        cameraDistance *= e.Delta > 0 ? 0.9 : 1.1;
        cameraDistance = Math.Clamp(cameraDistance, 20, 5_000);
        UpdateCamera();
    }

    private void UpdateCamera()
    {
        double yaw = DegreesToRadians(yawDegrees);
        double pitch = DegreesToRadians(pitchDegrees);

        var forward = new Vector3D(
            Math.Cos(pitch) * Math.Cos(yaw),
            Math.Cos(pitch) * Math.Sin(yaw),
            Math.Sin(pitch));

        camera.Position = sceneCenter - forward * cameraDistance;
        camera.LookDirection = forward * cameraDistance;
        camera.UpDirection = new Vector3D(0, 0, 1);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static int[] BuildSampleIndexArray(int sourceLength, int maxSamples)
    {
        maxSamples = Math.Max(2, maxSamples);

        if (sourceLength <= maxSamples)
        {
            int[] full = new int[sourceLength];
            for (int i = 0; i < sourceLength; i++)
            {
                full[i] = i;
            }

            return full;
        }

        int[] sampled = new int[maxSamples];
        double ratio = (double)(sourceLength - 1) / (maxSamples - 1);
        for (int i = 0; i < maxSamples; i++)
        {
            sampled[i] = Math.Clamp((int)Math.Round(i * ratio), 0, sourceLength - 1);
        }

        return sampled;
    }
}
