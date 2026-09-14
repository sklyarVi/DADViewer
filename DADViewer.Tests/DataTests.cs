namespace DADViewer.Tests;

public sealed class DataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dadviewer-tests-" + Guid.NewGuid().ToString("N"));
    public DataTests() => Directory.CreateDirectory(_directory);
    private string Write(double[]? times = null, float[]? waves = null, double[]? values = null)
    {
        times ??= [0, 2]; waves ??= [200, 230, 400]; values ??= [-1, 2, 3, 4, 5, 6];
        string path = Path.Combine(_directory, Guid.NewGuid() + ".dad");
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(waves.Length); writer.Write(times.Length);
        foreach (var x in times) writer.Write(x);
        foreach (var x in waves) writer.Write(x);
        foreach (var x in values) writer.Write(x);
        return path;
    }
    [Theory]
    [InlineData("TestData1.DAD", 4869)]
    [InlineData("TestData2.DAD", 3294)]
    [InlineData("TestData3.DAD", 4494)]
    public void ReadsRealSamples(string file, int spectra)
    {
        var data = new DADDataRepository().LoadFromFile(Path.Combine(AppContext.BaseDirectory, "SampleTestData", file));
        Assert.Equal(spectra, data.NSpect); Assert.Equal(106, data.NWaves);
        Assert.Equal(190, data.Wavelengths[0]); Assert.Equal(400, data.Wavelengths[^1]);
        Assert.True(data.MinIntensity < 0); Assert.True(data.MaxIntensity > 0);
    }
    [Fact]
    public void ReadsReadOnlyAndSharedReadFiles()
    {
        string path = Write(); File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(6, new DADDataRepository().LoadFromFile(path).GetIntensity(1, 2));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }
    [Fact]
    public void PreservesRowsColumnsAndNegativeSignals()
    {
        var data = new DADDataRepository().LoadFromFile(Write());
        Assert.Equal(new double[] { 4, 5, 6 }, data.GetSpectrum(1));
        Assert.Equal(new double[] { 2, 5 }, data.GetChromatogram(1));
        Assert.Equal(-1, data.MinIntensity); Assert.Equal(6, data.MaxIntensity);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    public void RejectsTruncation(int length)
    {
        string path = Write(); using (var s = new FileStream(path, FileMode.Open, FileAccess.Write)) s.SetLength(length);
        Assert.Throws<FormatException>(() => new DADDataRepository().LoadFromFile(path));
    }
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(20000, 20000)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RejectsInvalidOrOversizedHeaderBeforeAllocation(int waves, int spectra)
    {
        string path = Path.Combine(_directory, "header.dad");
        using (var writer = new BinaryWriter(File.Create(path))) { writer.Write(waves); writer.Write(spectra); }
        Assert.Throws<FormatException>(() => new DADDataRepository().LoadFromFile(path));
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsNonFiniteIntensity(double value)
        => Assert.Throws<FormatException>(() => new DADDataRepository().LoadFromFile(Write([0], [200], [value])));
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void RejectsInvalidTime(double value)
        => Assert.Throws<FormatException>(() => new DADDataRepository().LoadFromFile(Write([value], [200], [1])));
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(0)]
    public void RejectsInvalidWavelength(float value)
        => Assert.Throws<FormatException>(() => new DADDataRepository().LoadFromFile(Write([0], [value], [1])));
    [Fact]
    public void RejectsDuplicateOrUnsortedAxes()
    {
        Assert.Throws<FormatException>(() => new DADDataRepository().LoadFromFile(Write([1, 1])));
        Assert.Throws<FormatException>(() => new DADDataRepository().LoadFromFile(Write(waves: [200, 400, 230])));
    }
    [Fact]
    public void AllowsSinglePointAndConstantSignal()
    {
        var data = new DADDataRepository().LoadFromFile(Write([0], [200], [-2]));
        Assert.Equal(-2, data.MinIntensity); Assert.Equal(-2, data.MaxIntensity);
        Assert.Equal(0, data.FindTime(5)); Assert.Equal(0, data.FindWavelength(100));
    }
    [Fact]
    public void NearestIndexUsesPhysicalIrregularAxes()
    {
        var data = new DADDataRepository().LoadFromFile(Write());
        Assert.Equal(1, data.FindWavelength(240)); Assert.Equal(2, data.FindWavelength(399));
        Assert.Equal(0, data.FindTime(1)); Assert.Equal(1, data.FindTime(1.1));
        Assert.Equal(0, data.FindWavelength(-5)); Assert.Equal(2, data.FindWavelength(900));
    }
    [Fact]
    public void DatasetOwnsItsValues()
    {
        double[] times = [0, 2], waves = [200]; double[,] values = { { 1 }, { 2 } };
        var data = new DADData(times, waves, values);
        times[0] = 99; waves[0] = 999; values[0, 0] = 77;
        var slice = data.GetSpectrum(0); slice[0] = 55;
        Assert.Equal(0, data.TimeStamps[0]); Assert.Equal(200, data.Wavelengths[0]); Assert.Equal(1, data.GetIntensity(0, 0));
    }
    [Fact]
    public void RejectsWrongMatrixShapeAndSliceIndices()
    {
        Assert.Throws<ArgumentException>(() => new DADData([0], [200], new double[2, 1]));
        var data = new DADData([0], [200], new double[,] { { 1 } });
        Assert.Throws<ArgumentOutOfRangeException>(() => data.GetSpectrum(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.GetChromatogram(-1));
    }
    [Fact]
    public void CancellationIsObservedBeforeAndDuringReading()
    {
        var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => new DADDataRepository().LoadFromFile(Write(), cts.Token));
        using var during = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => new DADDataRepository().LoadFromFile(Write(), during.Token, new CancelProgress(during)));
    }
    private sealed class CancelProgress(CancellationTokenSource source) : IProgress<double>
    { public void Report(double value) => source.Cancel(); }
    [Fact]
    public void IgnoresTrailingBytesForExistingFormatCompatibility()
    {
        string path = Write(); using (var file = File.Open(path, FileMode.Append)) file.WriteByte(42);
        Assert.Equal(6, new DADDataRepository().LoadFromFile(path).GetIntensity(1, 2));
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
