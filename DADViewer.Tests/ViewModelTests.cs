using DADViewer.Application;
using DADViewer.Presentation.ViewModels;
namespace DADViewer.Tests;

public sealed class ViewModelTests
{
    private static DADData Data() => new([0, 2], [200, 230, 400], new double[,] { { -1, 2, 3 }, { 4, 5, 6 } });
    private sealed class Loader : ILoadDataService
    {
        public int Calls;
        public Func<CancellationToken, Task<DADData>> Load = token => Task.FromResult(Data());
        public Task<DADData> LoadAsync(string path, CancellationToken cancellationToken = default, IProgress<double>? progress = null)
        { Calls++; return Load(cancellationToken); }
    }
    [Fact]
    public async Task SelectionIsBoundedAndBothSlicesUseSamePoint()
    {
        using var vm = new MainViewModel(new Loader()); await vm.LoadAsync("test.dad");
        vm.Select(1, 2);
        Assert.Equal(6, vm.Data!.GetSpectrum(vm.TimeIndex)[vm.WavelengthIndex]);
        Assert.Equal(6, vm.Data.GetChromatogram(vm.WavelengthIndex)[vm.TimeIndex]);
        Assert.Equal(2, vm.MaxWavelengthIndex);
        vm.Select(999, -5); Assert.Equal(1, vm.TimeIndex); Assert.Equal(0, vm.WavelengthIndex);
    }
    [Fact]
    public async Task FailedLoadPreservesLastCompleteDatasetAndSelection()
    {
        var loader = new Loader(); using var vm = new MainViewModel(loader); await vm.LoadAsync("good.dad");
        vm.Select(1, 2); var old = vm.Data;
        loader.Load = _ => throw new FormatException("bad header"); await vm.LoadAsync("bad.dad");
        Assert.Same(old, vm.Data); Assert.Equal(1, vm.TimeIndex); Assert.Equal(2, vm.WavelengthIndex);
        Assert.True(vm.HasData); Assert.False(vm.IsBusy); Assert.Contains("bad header", vm.Status); Assert.Equal("good.dad", vm.FileName);
    }
    [Fact]
    public async Task CancelledLoadPreservesDataAndReentrantOpenIsIgnored()
    {
        var loader = new Loader(); using var vm = new MainViewModel(loader); await vm.LoadAsync("good.dad"); var old = vm.Data;
        loader.Load = async token => { await Task.Delay(Timeout.Infinite, token); return Data(); };
        var pending = vm.LoadAsync("slow.dad"); Assert.True(vm.IsBusy);
        await vm.LoadAsync("second.dad"); Assert.Equal(2, loader.Calls);
        vm.CancelLoading(); await pending;
        Assert.Same(old, vm.Data); Assert.False(vm.IsBusy); Assert.Contains("cancelled", vm.Status);
    }
    [Fact]
    public async Task ReloadResetsSelectionAndRangeForSmallerDataset()
    {
        var loader = new Loader(); using var vm = new MainViewModel(loader); await vm.LoadAsync("first.dad"); vm.Select(1, 2);
        loader.Load = _ => Task.FromResult(new DADData([1], [300], new double[,] { { 9 } })); await vm.LoadAsync("small.dad");
        Assert.Equal(0, vm.TimeIndex); Assert.Equal(0, vm.WavelengthIndex); Assert.Equal(0, vm.MaxTimeIndex);
        Assert.Equal(9, vm.ColorMinimum); Assert.Equal(9, vm.ColorMaximum);
    }
    [Fact]
    public async Task DisposeCancelsPendingLoad()
    {
        var loader = new Loader { Load = async token => { await Task.Delay(Timeout.Infinite, token); return Data(); } };
        var vm = new MainViewModel(loader); var pending = vm.LoadAsync("slow.dad"); vm.Dispose(); await pending;
        Assert.False(vm.IsBusy); Assert.False(vm.HasData);
    }
}
