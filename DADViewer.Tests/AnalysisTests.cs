using System.Globalization;
using System.Text;
using DADViewer.Application;
namespace DADViewer.Tests;

public sealed class AnalysisTests
{
    private static PeakOptions Options => new(0, 0, BaselineMode.Zero);
    [Fact]
    public void TriangleUsesPhysicalCoordinatesForAreaAndInterpolatedWidth()
    {
        var peak = Assert.Single(PeakAnalysis.Analyze([0, 1, 3], [0, 4, 0], Options).Peaks);
        Assert.Equal(1, peak.Position); Assert.Equal(4, peak.Height); Assert.Equal(6, peak.Area); Assert.Equal(1.5, peak.HalfHeightWidth);
        Assert.Equal(0, peak.Left); Assert.Equal(3, peak.Right);
    }
    [Fact]
    public void EndpointBaselineRemovesSlopeAndOffset()
    {
        var result = PeakAnalysis.Analyze([0, 1, 3], [5, 11, 11], Options with { Baseline = BaselineMode.EndpointLine });
        var peak = Assert.Single(result.Peaks); Assert.Equal(4, peak.Height); Assert.Equal(6, peak.Area);
        Assert.Equal(new double[] { 5, 7, 11 }, result.Baseline);
    }
    [Fact]
    public void NegativePeakHasPositiveHeightAndSignedArea()
    {
        var peak = Assert.Single(PeakAnalysis.Analyze([0, 1, 3], [0, -4, 0], Options with { Negative = true }).Peaks);
        Assert.Equal(4, peak.Height); Assert.Equal(-6, peak.Area); Assert.Equal(1.5, peak.HalfHeightWidth);
        Assert.Empty(PeakAnalysis.Analyze([0, 1, 3], [0, -4, 0], Options).Peaks);
    }
    [Fact]
    public void GaussianMatchesAnalyticWidthAndArea()
    {
        var x = Enumerable.Range(0, 1601).Select(i => -8 + i * 0.01).ToArray();
        var y = x.Select(value => Math.Exp(-value * value / 2)).ToArray();
        var peak = Assert.Single(PeakAnalysis.Analyze(x, y, Options).Peaks);
        Assert.InRange(peak.HalfHeightWidth!.Value, 2 * Math.Sqrt(2 * Math.Log(2)) - 0.0001, 2 * Math.Sqrt(2 * Math.Log(2)) + 0.0001);
        Assert.InRange(peak.Area, Math.Sqrt(2 * Math.PI) - 1e-10, Math.Sqrt(2 * Math.PI) + 1e-10);
    }
    [Fact]
    public void PlateauUsesMiddleIndexAndEndpointPeaksAreExcluded()
    {
        var peak = Assert.Single(PeakAnalysis.Analyze([0, 1, 2, 3], [0, 4, 4, 0], Options).Peaks);
        Assert.Equal(1, peak.Index); Assert.Equal(2, peak.HalfHeightWidth); Assert.Equal(8, peak.Area);
        Assert.Empty(PeakAnalysis.Analyze([0, 1, 2], [4, 2, 4], Options).Peaks);
        Assert.Empty(PeakAnalysis.Analyze([0, 1, 2], [4, 4, 4], Options).Peaks);
        Assert.Empty(PeakAnalysis.Analyze([0], [4], Options).Peaks);
    }
    [Fact]
    public void DistanceAndResultLimitRetainStrongestPeaks()
    {
        double[] x = [0, 1, 2, 3, 4, 5, 6], y = [0, 2, 0, 5, 0, 3, 0];
        var spaced = PeakAnalysis.Analyze(x, y, Options with { MinimumDistance = 3 });
        Assert.Equal(3, Assert.Single(spaced.Peaks).Position);
        var limited = PeakAnalysis.Analyze(x, y, Options with { MaximumPeaks = 1 });
        Assert.Equal(3, limited.DetectedCount); Assert.Equal(3, Assert.Single(limited.Peaks).Position);
        Assert.Equal(3, Assert.Single(PeakAnalysis.Analyze(x, y, Options with { MinimumProminence = 4 }).Peaks).Position);
    }
    [Fact]
    public void ShoulderWidthIsUnresolvedAndAreaStopsAtAdjacentValley()
    {
        var result = PeakAnalysis.Analyze([0, 1, 2, 3, 4], [0, 10, 8, 9, 0], Options);
        Assert.Equal(2, result.Peaks.Count); Assert.Null(result.Peaks[0].HalfHeightWidth); Assert.Null(result.Peaks[1].HalfHeightWidth);
        Assert.Equal(2, result.Peaks[0].Right); Assert.Equal(2, result.Peaks[1].Left);
        Assert.Equal(2, result.Peaks[0].LocalProminence); Assert.Equal(1, result.Peaks[1].LocalProminence);
    }
    [Fact]
    public void FlatValleyDividesAreasWithoutDoubleCounting()
    {
        var result = PeakAnalysis.Analyze([0, 1, 2, 3, 4, 5, 6], [0, 4, 2, 2, 2, 4, 0], Options);
        Assert.Equal(3, result.Peaks[0].Right); Assert.Equal(3, result.Peaks[1].Left); Assert.Equal(14, result.Peaks.Sum(p => p.Area));
    }
    [Fact]
    public void AreaClipsAtBaselineCrossings()
    {
        var peak = Assert.Single(PeakAnalysis.Analyze([0, 1, 2], [-2, 2, -2], Options).Peaks);
        Assert.Equal(1, peak.Area); Assert.Equal(0.5, peak.HalfHeightWidth);
    }
    [Fact]
    public void InvalidAxesThresholdsAndCancelledOperationsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => PeakAnalysis.Analyze([0, 0], [1, 2], Options));
        Assert.Throws<ArgumentException>(() => PeakAnalysis.Analyze([0, 1], [1, double.NaN], Options));
        Assert.Throws<ArgumentException>(() => PeakAnalysis.Analyze([0, 1], [1, 2], Options with { MinimumDistance = -1 }));
        Assert.Throws<OperationCanceledException>(() => PeakAnalysis.Analyze([0, 1], [1, 2], Options, new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => SignalComparison.Compare([0], [0], [0], [0], new CancellationToken(true)));
    }
    [Fact]
    public void ComparisonInterpolatesReferenceWithoutExtrapolation()
    {
        var same = SignalComparison.Compare([0, 1, 2, 3], [0, 1, 2, 3], [0, 3], [0, 3]);
        Assert.Equal(0, same.MeanAbsoluteError); Assert.Equal(0, same.RootMeanSquareError);
        var result = SignalComparison.Compare([0, 1, 2, 3], [0, 1, 2, 3], [1, 2], [4, 4]);
        Assert.Equal(2, result.Points.Count); Assert.Equal(-3, result.Points[0].Difference);
        Assert.Equal(2.5, result.MeanAbsoluteError); Assert.Equal(Math.Sqrt(6.5), result.RootMeanSquareError);
    }
    [Fact]
    public void ComparisonDistinguishesNoOverlapAndExactSinglePoint()
    {
        var none = SignalComparison.Compare([0, 1], [0, 1], [2], [2]);
        Assert.Empty(none.Points); Assert.Null(none.MeanAbsoluteError); Assert.Null(none.RootMeanSquareError);
        var one = SignalComparison.Compare([0, 1, 2], [0, 1, 2], [1], [5]);
        Assert.Single(one.Points); Assert.Equal(4, one.RootMeanSquareError);
    }
    [Fact]
    public void ComparisonRmseAvoidsSquaringOverflow()
    {
        var result = SignalComparison.Compare([0, 1], [1e200, 1e200], [0, 1], [0, 0]);
        Assert.Equal(1e200, result.RootMeanSquareError);
    }
    [Fact]
    public void SliceSelectionReportsActualNearestReferenceCoordinateAndRejectsMissingCoverage()
    {
        var data = new DADData([0, 1, 2], [200, 220], new double[,] { { 0, 0 }, { 4, 2 }, { 0, 0 } });
        var reference = new DADData([0, 2], [190, 210, 230], new double[,] { { 0, 0, 0 }, { 0, 0, 0 } });
        var request = new AnalysisRequest(data, "a.dad", false, 200, Options, reference, "b.dad");
        var result = SliceAnalysis.Analyze(request);
        Assert.Equal(200, result.Coordinate); Assert.Equal(190, result.ReferenceCoordinate); Assert.NotNull(result.Comparison);
        var absent = new DADData([0, 2], [300], new double[,] { { 0 }, { 0 } });
        Assert.Null(SliceAnalysis.Analyze(request with { Reference = absent }).ReferenceX);
        Assert.Throws<ArgumentException>(() => SliceAnalysis.Analyze(request with { Coordinate = 199 }));
    }
    [Fact]
    public void EmptyPeakExportStillRecordsParametersAndZeroCount()
    {
        var data = new DADData([0, 1], [200], new double[,] { { 0 }, { 0 } });
        var result = SliceAnalysis.Analyze(new(data, "empty.dad", false, 200, Options));
        using var stream = new MemoryStream(); AnalysisCsv.Write(stream, result, false);
        var lines = Encoding.UTF8.GetString(stream.ToArray()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length); Assert.Equal(lines[0].Split(',').Length, lines[1].Split(',').Length);
        Assert.Contains(",500,0,0,,,,,,,,", lines[1]);
    }
    [Fact]
    public void AnalysisCsvKeepsPrecisionParametersAndEscapedFileNames()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            var data = new DADData([0, 1, 3], [200], new double[,] { { 0 }, { 4 }, { 0 } });
            var result = SliceAnalysis.Analyze(new(data, "=name,quoted.dad", false, 200, Options, data, "ref.dad"));
            using var stream = new MemoryStream(); AnalysisCsv.Write(stream, result, false);
            string csv = Encoding.UTF8.GetString(stream.ToArray());
            Assert.Contains("\"'=name,quoted.dad\"", csv); Assert.Contains(",Zero,False,0,0,500,1,1,1,4,4,4,0,3,6,1.5", csv);
            using var comparison = new MemoryStream(); AnalysisCsv.Write(comparison, result, true);
            Assert.Equal(4, Encoding.UTF8.GetString(comparison.ToArray()).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Length);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
