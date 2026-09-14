using DADViewer.Infrastructure;
namespace DADViewer.Console;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1) { System.Console.Error.WriteLine("Usage: DADViewer.Console <file.dad|file.uv|Waters.raw|file.arw|Chromeleon.txt>"); return 2; }
        try
        {
            var data = new ChromatographyDataReader().LoadFromFile(args[0]);
            System.Console.WriteLine($"Spectra: {data.NSpect}; wavelengths: {data.NWaves}; intensity: {data.MinIntensity:G6} .. {data.MaxIntensity:G6} {data.Metadata.IntensityUnit}; format: {data.Metadata.Format}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        { System.Console.Error.WriteLine(ex.Message); return 1; }
    }
}
