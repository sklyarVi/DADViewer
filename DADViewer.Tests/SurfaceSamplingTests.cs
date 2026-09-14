using DADViewer.Presentation.Rendering;
using DADViewer.Presentation.Views;
namespace DADViewer.Tests;

public sealed class SurfaceSamplingTests
{
    [Fact]
    public void RetainsNarrowPositiveAndNegativePeaksAtTheirMeasuredCoordinates()
    {
        var values = new double[2001, 301]; values[999, 137] = 17; values[1703, 243] = -11;
        var data = new DADData(Enumerable.Range(0, 2001).Select(i => i / 7d).ToArray(), Enumerable.Range(0, 301).Select(i => 190 + i * 0.7).ToArray(), values);
        var samples = SurfaceSampler.Select(data, 128);
        Assert.Contains(999, samples.Times); Assert.Contains(137, samples.Wavelengths);
        Assert.Contains(1703, samples.Times); Assert.Contains(243, samples.Wavelengths);
        var mesh = DAD3DView.BuildMesh(data, new ColorScale(-11, 17, 32, ColorScheme.Viridis));
        Assert.Equal(4, mesh.Positions.Max(p => p.Z)); Assert.Equal(0, mesh.Positions.Min(p => p.Z));
        Assert.Contains(mesh.Positions, p => Math.Abs(p.X - 999 / 2000d * 10) < 1e-12 && Math.Abs(p.Y - 137 / 300d * 10) < 1e-12 && p.Z == 4);
        for (int t = 0; t < 128; t++) for (int w = 0; w < 128; w++)
            Assert.Equal(ColorMap.Normalize(data.GetIntensity(samples.Times[t], samples.Wavelengths[w]), -11, 17) * 4, mesh.Positions[t * 128 + w].Z);
    }
    [Fact]
    public void TiedExtremaStillRetainAnActualMaximumIntersection()
    {
        var values = new double[401, 401]; values[17, 319] = 10; values[321, 19] = 10; values[200, 200] = -5;
        var data = new DADData(Enumerable.Range(0,401).Select(i=>(double)i).ToArray(), Enumerable.Range(1,401).Select(i=>(double)i).ToArray(), values);
        var mesh = DAD3DView.BuildMesh(data, new ColorScale(-5,10,32,ColorScheme.Viridis));
        Assert.Equal(4,mesh.Positions.Max(p=>p.Z)); Assert.Equal(0,mesh.Positions.Min(p=>p.Z));
    }
    [Fact]
    public void FlatDataSamplingIsBoundedSortedDeterministicAndIncludesEndpoints()
    {
        var data = new DADData(Enumerable.Range(0, 1000).Select(i => (double)i).ToArray(), [200, 205], new double[1000, 2]);
        var first = SurfaceSampler.Select(data, 128); var second = SurfaceSampler.Select(data, 128);
        Assert.Equal(128, first.Times.Length); Assert.Equal(new[] {0,1}, first.Wavelengths);
        Assert.Equal(0, first.Times[0]); Assert.Equal(999, first.Times[^1]);
        Assert.Equal(first.Times.Order().Distinct(), first.Times); Assert.Equal(first.Times, second.Times);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => SurfaceSampler.Select(data,128,cts.Token));
    }
    [Fact]
    public void SmallAndSingleAxisDataKeepEverySample()
    {
        var data = new DADData([1], [200,210,215], new double[,] {{1,2,3}});
        var samples = SurfaceSampler.Select(data,128);
        Assert.Equal(new[] {0},samples.Times); Assert.Equal(new[] {0,1,2},samples.Wavelengths);
    }
}
