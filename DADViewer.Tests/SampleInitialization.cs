using System.Runtime.CompilerServices;
using DADViewer.Samples;
namespace DADViewer.Tests;

internal static class SampleInitialization
{
    [ModuleInitializer]
    internal static void Initialize() => SyntheticSamples.WriteAll(Path.Combine(AppContext.BaseDirectory, "SampleTestData"));
}
