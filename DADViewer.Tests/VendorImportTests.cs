using System.Buffers.Binary;
using System.Text;
using DADViewer.Application;
using DADViewer.Domain;
using DADViewer.Infrastructure.Import;
namespace DADViewer.Tests;

public sealed class VendorImportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "dad-vendors-" + Guid.NewGuid().ToString("N"));
    public VendorImportTests() => Directory.CreateDirectory(_directory);
    internal static void WriteUv(string path, bool modern = true, bool array = false, string unit = "mAU")
    {
        using var writer = new BinaryWriter(File.Create(path)); int start = modern ? 4096 : 512;
        writer.Write(new byte[start]);
        void Pascal(int offset, string text, bool unicode) { writer.BaseStream.Position = offset; writer.Write((byte)text.Length); writer.Write((unicode ? Encoding.Unicode : Encoding.Latin1).GetBytes(text)); }
        Pascal(0, modern ? "131" : "31", false); if (modern) Pascal(0x15B, array ? "OL DATA FILE" : "LC DATA FILE", true);
        writer.BaseStream.Position = 0x116; writer.Write(BinaryPrimitives.ReverseEndianness(3u));
        writer.BaseStream.Position = modern ? 0xC0D : 0x13E; writer.Write(BinaryPrimitives.ReverseEndianness(BitConverter.DoubleToInt64Bits(0.25)));
        Pascal(modern ? 0xC15 : 0x146, unit, modern); writer.BaseStream.Position = start;
        for (int t = 0; t < 3; t++)
        {
            writer.Write((ushort)(array ? 70 : 67)); writer.Write((ushort)(array ? 46 : 32)); writer.Write((uint)(t * 30000));
            writer.Write((ushort)4001); writer.Write((ushort)4041); writer.Write((ushort)20); writer.Write(0L);
            if (array) { writer.Write((double)(t - 4)); writer.Write((double)(t + 1)); writer.Write((double)(t - 1)); }
            else { writer.Write(short.MinValue); writer.Write(t - 4); writer.Write((short)5); writer.Write((short)-2); }
        }
        writer.Write(0);
    }
    private string Uv(bool modern = true, bool array = false, string unit = "mAU") { string path = Path.Combine(_directory, "test.uv"); WriteUv(path, modern, array, unit); return path; }
    private static void Change(string path, long offset, Action<BinaryWriter> write) { using var writer = new BinaryWriter(File.Open(path, FileMode.Open, FileAccess.Write)); writer.BaseStream.Position = offset; write(writer); }
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(true, true)]
    public void AgilentVariantsPreserveFractionalWavelengthsTimesNegativeSignalsAndScale(bool modern, bool array)
    {
        var data = new ChromatographyDataReader().LoadFromFile(Uv(modern, array));
        Assert.Equal(new double[] { 0, 0.5, 1 }, data.TimeStamps); Assert.Equal(new double[] { 200.05, 201.05, 202.05 }, data.Wavelengths);
        Assert.Equal(new double[] { -1, 0.25, -0.25 }, data.GetSpectrum(0)); Assert.Equal("mAU", data.Metadata.IntensityUnit);
    }
    [Fact]
    public void AgilentAuAndMicroAuConvertToMilliAbsorbanceUnits()
    {
        Assert.Equal(-1000, new AgilentUvReader().Read(Uv(unit: "AU")).GetIntensity(0, 0));
        Assert.Equal(-0.001, new AgilentUvReader().Read(Uv(unit: "µAU")).GetIntensity(0, 0));
    }
    [Fact]
    public void AgilentRejectsChangingGridBadLengthsPartialCountsAndExcessiveDimensions()
    {
        string path = Uv(); Change(path, 4096 + 32 + 8, w => w.Write((ushort)4021)); Assert.Throws<FormatException>(() => new AgilentUvReader().Read(path));
        path = Uv(); Change(path, 4098, w => w.Write((ushort)31)); Assert.Throws<FormatException>(() => new AgilentUvReader().Read(path));
        path = Uv(); Change(path, 0x116, w => w.Write(0)); var recovered = new AgilentUvReader().Read(path); Assert.Equal(3, recovered.NSpect); Assert.Contains("unfinalized", recovered.Metadata.Format);
        path = Uv(); Change(path, 0x116, w => w.Write(BinaryPrimitives.ReverseEndianness(uint.MaxValue))); Assert.Throws<FormatException>(() => new AgilentUvReader().Read(path));
        path = Uv(unit: "counts"); Assert.Throws<FormatException>(() => new AgilentUvReader().Read(path));
    }
    [Fact]
    public void UnfinalizedUvRecoversOnlyCompleteSegmentsAndReportsDiscardedTail()
    {
        string path = Uv(); Change(path, 0x116, w => w.Write(0));
        using (var stream = File.OpenWrite(path)) stream.SetLength(4096 + 32 * 2 + 25);
        var data = new AgilentUvReader().Read(path); Assert.Equal(2, data.NSpect); Assert.Contains("omitted", data.Metadata.Detail);
        path = Uv(); using (var stream = File.OpenWrite(path)) stream.SetLength(4096 + 32 * 2 + 25);
        Assert.Throws<FormatException>(() => new AgilentUvReader().Read(path));
    }
    private string Waters(int type = 12)
    {
        string folder = Path.Combine(_directory, "example.raw"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "_FUNCTNS.INF"), new byte[416]); Change(Path.Combine(folder, "_FUNCTNS.INF"), 0, w => w.Write((byte)type));
        using var index = new BinaryWriter(File.Create(Path.Combine(folder, "_FUNC001.IDX")));
        string path = Path.Combine(folder, "_FUNC001.DAT"); using var data = new BinaryWriter(File.Create(path));
        for (int t = 0; t < 3; t++)
        {
            index.Write((uint)(t * 18)); index.Write(0xFFC00003u); index.Write(0); index.Write(t * 0.5f); index.Write(new byte[6]);
            for (int w = 0; w < 3; w++)
            {
                data.Write((short)(t + w - 2));
                // wavelength = (200 + w + .25); exponent -15, signed intensity multiplied by 4.
                uint key = (uint)((200 + w + 0.25) * 32768); data.Write((key << 9) | (8u << 4) | 1u);
            }
        }
        return path;
    }
    [Fact]
    public void WatersUsesIndexOffsetsFunctionTypeAndFractionalWavelengths()
    {
        string path = Waters(); var data = new ChromatographyDataReader().LoadFromFile(Path.GetDirectoryName(path)!);
        Assert.Equal(new double[] { 200.25, 201.25, 202.25 }, data.Wavelengths); Assert.Equal(new double[] { 0, 0.5, 1 }, data.TimeStamps);
        Assert.Equal(new double[] { -8, -4, 0 }, data.GetSpectrum(0)); Assert.Equal(Path.GetFullPath(path), data.Metadata.ResolvedPath);
        Assert.Contains("raw", data.Metadata.IntensityUnit); Assert.Contains("_FUNC001.IDX", data.Metadata.Fingerprint);
    }
    [Fact]
    public void WatersRejectsMsEvenIfPolarityOrOtherMetadataAreAbsent()
    {
        string path = Waters(9); var error = Assert.Throws<FormatException>(() => new WatersPdaReader().Read(path)); Assert.Contains("not a confirmed", error.Message);
        Assert.Throws<FormatException>(() => new ChromatographyDataReader().LoadFromFile(Path.GetDirectoryName(path)!));
    }
    [Fact]
    public void WatersRejectsMissingCompanionsBadOffsetsAndChangingAxes()
    {
        string path = Waters(); Change(Path.ChangeExtension(path, ".IDX"), 22, w => w.Write(0u)); Assert.Throws<FormatException>(() => new WatersPdaReader().Read(path));
        path = Waters(); Change(path, 18 + 2, w => w.Write(0u)); Assert.Throws<FormatException>(() => new WatersPdaReader().Read(path));
        path = Waters(); File.Delete(Path.ChangeExtension(path, ".IDX")); Assert.Throws<FormatException>(() => new WatersPdaReader().Read(path));
    }
    internal static string ChromeleonText => "Generating Data System\tChromeleon 7.2\nDetector\tUV\nScan Min. (nm)\t200\nSignal Min. (mAU)\t-1\nSpectra\t3\nDilution Factor\t1.0000\nRaw Data:\nTime (min)\tIntegr.Time (s)\t200\t202\n0\t1\t-1\t2\n0.5\t1\t3\t4\n1\t1\t5\t6\n";
    private string TextFile(string content, string extension = ".txt", Encoding? encoding = null) { string path = Path.Combine(_directory, "spectrum" + extension); File.WriteAllText(path, content, encoding ?? new UTF8Encoding(false)); return path; }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ChromeleonReadsConfirmedSpectralAsciiIncludingUtf16(bool unicode)
    {
        var data = new ChromatographyDataReader().LoadFromFile(TextFile(ChromeleonText, encoding: unicode ? Encoding.Unicode : Encoding.UTF8));
        Assert.Equal(3, data.NSpect); Assert.Equal(2, data.NWaves); Assert.Equal(-1, data.GetIntensity(0, 0)); Assert.Equal(1, data.TimeStamps[^1]); Assert.Equal("mAU", data.Metadata.IntensityUnit);
    }
    [Fact]
    public void ChromeleonHandlesCommaDecimalsSecondsAndAu()
    {
        string text = ChromeleonText.Replace("1.0000", "1,0000").Replace("0.5", "30,0").Replace("\n1\t1\t5", "\n60\t1\t5").Replace("Time (min)", "Time (s)").Replace("(mAU)", "(AU)");
        var data = new SpectralTextReader().Read(TextFile(text), false); Assert.Equal(0.5, data.TimeStamps[1]); Assert.Equal(1, data.TimeStamps[2]); Assert.Equal(-1000, data.GetIntensity(0, 0));
    }
    [Theory]
    [InlineData("Spectra\t3", "Spectra\t4")]
    [InlineData("\t3\t4", "\t3")]
    [InlineData("Detector\tUV", "Detector\tMS")]
    [InlineData("Signal Min. (mAU)\t-1", "Signal Min. (mAU)\t-1\nSignal Min. (AU)\t-1")]
    [InlineData("Time (min)", "Time (unknown)")]
    [InlineData("\t3\t4", "\tNaN\t4")]
    [InlineData("\t200\t202", "\t202\t200")]
    public void ChromeleonRejectsWrongDimensionsSignalsAndMissingValues(string from, string to)
        => Assert.Throws<FormatException>(() => new SpectralTextReader().Read(TextFile(ChromeleonText.Replace(from, to)), false));
    [Fact]
    public void EmpowerPdaKeepsNonuniformAxesAndNormalizesAu()
    {
        string text = "\"Det. Units\"\t\"Channel Type\"\t\"Source S/W Info\"\n\"AU\"\t\"3D PDA/FLR\"\t\"Empower 3\"\nWavelength\t210.123\t211.456\nTime\n0\t0.001\t-0.002\n0.1\t0.003\t0.004\n";
        var data = new ChromatographyDataReader().LoadFromFile(TextFile(text, ".arw"));
        Assert.Equal(new double[] { 210.123, 211.456 }, data.Wavelengths); Assert.Equal(-2, data.GetIntensity(0, 1)); Assert.Equal("mAU", data.Metadata.IntensityUnit);
    }
    [Fact]
    public void FolderAmbiguityUnknownFormatsAndCancellationAreExplicit()
    {
        string path = Uv(); File.Copy(path, Path.Combine(_directory, "second.uv"));
        Assert.Contains("Multiple", Assert.Throws<FormatException>(() => new ChromatographyDataReader().LoadFromFile(_directory)).Message);
        Assert.Throws<OperationCanceledException>(() => new AgilentUvReader().Read(path, new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => new WatersPdaReader().Read(Waters(), new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => new SpectralTextReader().Read(TextFile(ChromeleonText), false, new CancellationToken(true)));
        Assert.Contains("Native Chromeleon", Assert.Throws<FormatException>(() => new ChromatographyDataReader().LoadFromFile(TextFile("foreign binary data", ".dad"))).Message);
    }
    [Fact]
    public void ComparisonRejectsIncompatibleIntensityScalesWithoutLosingPrimaryPeaks()
    {
        var known = new DADData([0, 1, 2], [200], new double[,] { { 0 }, { 2 }, { 0 } }, metadata: new("Agilent UV", "mAU"));
        var raw = new DADData([0, 1, 2], [200], new double[,] { { 0 }, { 2 }, { 0 } }, metadata: new("Waters PDA", "Waters raw absorbance"));
        var result = SliceAnalysis.Analyze(new(known, "a.uv", false, 200, new(0, 0), raw, "b.dat"));
        Assert.Single(result.Peaks.Peaks); Assert.Null(result.Comparison); Assert.Null(result.ReferenceY); Assert.Contains("Incompatible", result.ComparisonUnavailableReason);
        Assert.NotNull(SliceAnalysis.Analyze(new(known, "a.uv", false, 200, new(0, 0), known, "b.txt")).Comparison);
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
