using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using DADViewer.Domain;
using DADViewer.Presentation.Rendering;
using System.Windows.Input;
namespace DADViewer.Presentation.Views;

public partial class DAD3DView : UserControl
{
    public const int MaxSamplesPerAxis = 128;
    public int VertexCount { get; private set; }
    private ModelVisual3D? _surface;
    private DADData? _data;
    private double _yaw = -65, _pitch = 35, _distance = 40;
    private Point? _lastPointer;
    private bool _fitCamera;
    public DAD3DView() { InitializeComponent(); Viewport.SizeChanged += (_, _) => { if (_fitCamera) FitCamera(); }; }
    private void FitCamera()
    {
        if (Viewport.ActualWidth <= 0 || Viewport.ActualHeight <= 0) return;
        double aspect = Viewport.ActualWidth / Viewport.ActualHeight;
        double angle = Math.Min(Math.PI / 8, Math.Atan(Math.Tan(Math.PI / 8) / aspect));
        _distance = 8 / Math.Sin(angle); _fitCamera = false; UpdateCamera();
    }
    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180, pitch = _pitch * Math.PI / 180;
        var offset = new Vector3D(Math.Cos(pitch) * Math.Cos(yaw), Math.Cos(pitch) * Math.Sin(yaw), Math.Sin(pitch)) * _distance;
        Viewport.Camera = new PerspectiveCamera(new Point3D(5, 5, 2) + offset, -offset, new Vector3D(0, 0, 1), 45) { NearPlaneDistance = 0.1, FarPlaneDistance = 2000 };
    }
    private void ResetCamera(object sender, RoutedEventArgs e) { _yaw = -65; _pitch = 35; _fitCamera = true; FitCamera(); }
    private void BeginRotate(object sender, MouseButtonEventArgs e)
    {
        if (_surface == null) return;
        var element = (UIElement)sender; _lastPointer = e.GetPosition(element); element.CaptureMouse(); e.Handled = true;
    }
    private void EndRotate(object sender, MouseButtonEventArgs e) { _lastPointer = null; ((UIElement)sender).ReleaseMouseCapture(); }
    private void Rotate(object sender, MouseEventArgs e)
    {
        if (_lastPointer is not { } previous || e.RightButton != MouseButtonState.Pressed) return;
        var point = e.GetPosition((UIElement)sender);
        _yaw += (point.X - previous.X) * 0.4; _pitch = Math.Clamp(_pitch + (point.Y - previous.Y) * 0.4, -85, 85);
        _lastPointer = point; UpdateCamera();
    }
    private void Zoom(object sender, MouseWheelEventArgs e)
    {
        if (_surface == null) return;
        _distance = Math.Clamp(_distance * Math.Pow(1.15, -e.Delta / 120d), 8, 500); UpdateCamera(); e.Handled = true;
    }
    public void Clear()
    {
        if (_surface != null) Viewport.Children.Remove(_surface);
        _surface = null; _data = null; VertexCount = 0; Hint.Text = "Enable 3D after loading a file";
    }
    public async Task RenderAsync(DADData data, ColorScale scale, CancellationToken token)
    {
        if (data.NSpect < 2 || data.NWaves < 2) { Clear(); Hint.Text = "A 3D surface requires at least two points on each axis."; return; }
        var model = await Task.Run(() =>
        {
            var mesh = BuildMesh(data, scale, token);
            var pixels = new byte[256 * 4];
            for (int i = 0; i < 256; i++) { var c = ColorMap.Get(i / 255d, scale.Steps, scale.Scheme); pixels[i * 4] = c.B; pixels[i * 4 + 1] = c.G; pixels[i * 4 + 2] = c.R; pixels[i * 4 + 3] = 255; }
            var texture = BitmapSource.Create(256, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 1024); texture.Freeze();
            var brush = new ImageBrush(texture) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) }; brush.Freeze();
            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(Brushes.Black)); // Supplies opaque alpha for the emissive color layer.
            material.Children.Add(new EmissiveMaterial(brush)); material.Freeze();
            var result = new GeometryModel3D(mesh, material) { BackMaterial = material }; result.Freeze(); return result;
        }, token);
        token.ThrowIfCancellationRequested();
        bool reset = !ReferenceEquals(_data, data);
        if (_surface != null) Viewport.Children.Remove(_surface);
        _surface = new ModelVisual3D { Content = model }; Viewport.Children.Add(_surface);
        VertexCount = ((MeshGeometry3D)model.Geometry).Positions.Count; _data = data;
        Hint.Text = $"Adaptive 3D preview · {VertexCount:N0} vertices · global min/max retained · normalized axes: X=time, Y=wavelength, Z=intensity\nRotate: right drag · Zoom: mouse wheel · Exact values: linked 2D views";
        if (reset) { _yaw = -65; _pitch = 35; _fitCamera = true; UpdateCamera(); FitCamera(); }
    }
    public static MeshGeometry3D BuildMesh(DADData data, ColorScale scale, CancellationToken token = default)
    {
        var samples = SurfaceSampler.Select(data, MaxSamplesPerAxis, token);
        int nt = samples.Times.Length, nw = samples.Wavelengths.Length;
        var mesh = new MeshGeometry3D();
        for (int t = 0; t < nt; t++)
        {
            token.ThrowIfCancellationRequested();
            int ti = samples.Times[t];
            for (int w = 0; w < nw; w++)
            {
                int wi = samples.Wavelengths[w];
                double intensity = data.GetIntensity(ti, wi);
                mesh.Positions.Add(new Point3D(ColorMap.Normalize(data.TimeStamps[ti], data.TimeStamps[0], data.TimeStamps[^1]) * 10,
                    ColorMap.Normalize(data.Wavelengths[wi], data.Wavelengths[0], data.Wavelengths[^1]) * 10,
                    ColorMap.Normalize(intensity, data.MinIntensity, data.MaxIntensity) * 4));
                mesh.TextureCoordinates.Add(new Point(ColorMap.Normalize(intensity, scale.Minimum, scale.Maximum), 0.5));
            }
        }
        for (int t = 0; t < nt - 1; t++) for (int w = 0; w < nw - 1; w++)
        {
            int a = t * nw + w, b = a + nw;
            mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
            mesh.TriangleIndices.Add(a + 1); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1);
        }
        var normals = new Vector3D[mesh.Positions.Count];
        for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
        {
            int a = mesh.TriangleIndices[i], b = mesh.TriangleIndices[i + 1], c = mesh.TriangleIndices[i + 2];
            var normal = Vector3D.CrossProduct(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
            normals[a] += normal; normals[b] += normal; normals[c] += normal;
        }
        foreach (var value in normals) { var normal = value; if (normal.LengthSquared > 0) normal.Normalize(); else normal = new Vector3D(0, 0, 1); mesh.Normals.Add(normal); }
        mesh.Freeze(); return mesh;
    }
}
