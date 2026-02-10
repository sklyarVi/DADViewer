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
    private DADData? currentDadData;

    private Point? lastMousePosition;
    private Point3D sceneCenter = new(50, 50, 50);
    private double cameraDistance = 250;
    private double yawDegrees = 45;
    private double pitchDegrees = -35;
    private bool showGrid = true;

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
    public bool ShowGrid
    {
        get => showGrid;
        set
        {
            if (showGrid == value)
            {
                return;
            }

            showGrid = value;
            if (currentDadData != null)
            {
                Render3DData(currentDadData);
            }
        }
    }

    public void Render3DData(DADData dadData)
    {
        currentDadData = dadData;

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
        double maxAbsIntensity = Math.Max(Math.Abs(minIntensity), Math.Abs(maxIntensity));

        if (waveRange == 0)
        {
            waveRange = 1;
        }

        if (timeRange == 0)
        {
            timeRange = 1;
        }

        if (maxAbsIntensity == 0)
        {
            maxAbsIntensity = 1;
        }

        const double xScale = 100;
        const double yScale = 100;
        const double zScale = 70;
        double dataMinZ = (minIntensity / maxAbsIntensity) * zScale;
        double dataMaxZ = (maxIntensity / maxAbsIntensity) * zScale;

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
                double z = (dadData.Intensities[sourceRow, sourceColumn] / maxAbsIntensity) * zScale;

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

        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
            Opacity = 0.96
        };
        gradient.GradientStops.Add(new GradientStop(Colors.SteelBlue, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Gold, 1));

        var surfaceMaterial = new MaterialGroup();
        surfaceMaterial.Children.Add(new DiffuseMaterial(gradient));
        surfaceMaterial.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 50));

        var surfaceModel = new GeometryModel3D
        {
            Geometry = mesh,
            Material = surfaceMaterial,
            BackMaterial = surfaceMaterial
        };

        sceneGroup.Children.Clear();
        sceneGroup.Children.Add(new AmbientLight(Colors.Gray));
        sceneGroup.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, -0.3, -1)));

        AddAxesAndGrid(sceneGroup, xScale, yScale, dataMinZ, dataMaxZ, showGrid);
        sceneGroup.Children.Add(surfaceModel);

        Rect3D bounds = mesh.Bounds;
        const double zeroPlane = 0;
        double minZBound = Math.Min(bounds.Z, zeroPlane);
        double maxZBound = Math.Max(bounds.Z + bounds.SizeZ, zeroPlane);
        sceneCenter = new Point3D(
            bounds.X + bounds.SizeX / 2,
            bounds.Y + bounds.SizeY / 2,
            (minZBound + maxZBound) / 2);

        double fullZSize = maxZBound - minZBound;
        double maxSize = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, fullZSize));
        cameraDistance = Math.Max(140, maxSize * 2.2);
        yawDegrees = 45;
        pitchDegrees = -35;
        UpdateCamera();
    }

    private static void AddAxesAndGrid(Model3DGroup group, double xScale, double yScale, double zMin, double zMax, bool showGrid)
    {
        const int gridLines = 10;
        const double gridThickness = 0.12;
        const double axisThickness = 0.45;
        const double floorZ = 0;

        var gridColor = Color.FromArgb(160, 130, 145, 166);
        var xAxisColor = Color.FromRgb(226, 72, 78);
        var yAxisColor = Color.FromRgb(84, 172, 98);
        var zAxisColor = Color.FromRgb(70, 125, 226);

        if (showGrid)
        {
            for (int i = 0; i <= gridLines; i++)
            {
                double x = i * xScale / gridLines;
                double y = i * yScale / gridLines;

                group.Children.Add(CreateAxisAlignedLineModel(
                    new Point3D(x, 0, floorZ),
                    new Point3D(x, yScale, floorZ),
                    gridThickness,
                    gridColor));

                group.Children.Add(CreateAxisAlignedLineModel(
                    new Point3D(0, y, floorZ),
                    new Point3D(xScale, y, floorZ),
                    gridThickness,
                    gridColor));
            }
        }

        group.Children.Add(CreateAxisAlignedLineModel(new Point3D(0, 0, floorZ), new Point3D(xScale * 1.05, 0, floorZ), axisThickness, xAxisColor));
        group.Children.Add(CreateAxisAlignedLineModel(new Point3D(0, 0, floorZ), new Point3D(0, yScale * 1.05, floorZ), axisThickness, yAxisColor));

        double zAxisMin = Math.Min(zMin, 0) * 1.05;
        double zAxisMax = Math.Max(zMax, 0) * 1.05;
        if (AreClose(zAxisMin, zAxisMax))
        {
            zAxisMin -= 1;
            zAxisMax += 1;
        }

        group.Children.Add(CreateAxisAlignedLineModel(new Point3D(0, 0, zAxisMin), new Point3D(0, 0, zAxisMax), axisThickness, zAxisColor));
    }

    private static GeometryModel3D CreateAxisAlignedLineModel(Point3D from, Point3D to, double thickness, Color color)
    {
        MeshGeometry3D mesh = BuildAxisAlignedLineMesh(from, to, thickness);

        var material = new DiffuseMaterial(new SolidColorBrush(color));
        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material
        };
    }

    private static MeshGeometry3D BuildAxisAlignedLineMesh(Point3D from, Point3D to, double thickness)
    {
        double half = thickness / 2.0;

        Rect3D rect;
        if (!AreClose(from.X, to.X))
        {
            double minX = Math.Min(from.X, to.X);
            double length = Math.Abs(to.X - from.X);
            rect = new Rect3D(minX, from.Y - half, from.Z - half, length, thickness, thickness);
        }
        else if (!AreClose(from.Y, to.Y))
        {
            double minY = Math.Min(from.Y, to.Y);
            double length = Math.Abs(to.Y - from.Y);
            rect = new Rect3D(from.X - half, minY, from.Z - half, thickness, length, thickness);
        }
        else
        {
            double minZ = Math.Min(from.Z, to.Z);
            double length = Math.Abs(to.Z - from.Z);
            rect = new Rect3D(from.X - half, from.Y - half, minZ, thickness, thickness, length);
        }

        return BuildBoxMesh(rect);
    }

    private static MeshGeometry3D BuildBoxMesh(Rect3D rect)
    {
        double x0 = rect.X;
        double y0 = rect.Y;
        double z0 = rect.Z;
        double x1 = rect.X + rect.SizeX;
        double y1 = rect.Y + rect.SizeY;
        double z1 = rect.Z + rect.SizeZ;

        var p0 = new Point3D(x0, y0, z0);
        var p1 = new Point3D(x1, y0, z0);
        var p2 = new Point3D(x1, y1, z0);
        var p3 = new Point3D(x0, y1, z0);
        var p4 = new Point3D(x0, y0, z1);
        var p5 = new Point3D(x1, y0, z1);
        var p6 = new Point3D(x1, y1, z1);
        var p7 = new Point3D(x0, y1, z1);

        var mesh = new MeshGeometry3D();
        AddFace(mesh, p0, p1, p2, p3); // bottom
        AddFace(mesh, p4, p5, p6, p7); // top
        AddFace(mesh, p0, p1, p5, p4); // front
        AddFace(mesh, p1, p2, p6, p5); // right
        AddFace(mesh, p2, p3, p7, p6); // back
        AddFace(mesh, p3, p0, p4, p7); // left
        return mesh;
    }

    private static void AddFace(MeshGeometry3D mesh, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
    {
        int start = mesh.Positions.Count;
        mesh.Positions.Add(p0);
        mesh.Positions.Add(p1);
        mesh.Positions.Add(p2);
        mesh.Positions.Add(p3);

        mesh.TriangleIndices.Add(start + 0);
        mesh.TriangleIndices.Add(start + 1);
        mesh.TriangleIndices.Add(start + 2);

        mesh.TriangleIndices.Add(start + 0);
        mesh.TriangleIndices.Add(start + 2);
        mesh.TriangleIndices.Add(start + 3);
    }

    private static bool AreClose(double a, double b)
    {
        return Math.Abs(a - b) < 0.000001;
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
