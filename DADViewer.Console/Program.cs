using DADViewer.Infrastructure;
namespace DADViewer.Console;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 1) { System.Console.Error.WriteLine("Usage: DADViewer.Console <file.dad>"); return 2; }
        try
        {
            var data = new DADDataRepository().LoadFromFile(args[0]);
            System.Console.WriteLine($"Spectra: {data.NSpect}; wavelengths: {data.NWaves}; intensity: {data.MinIntensity:G6} .. {data.MaxIntensity:G6}");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        { System.Console.Error.WriteLine(ex.Message); return 1; }
    }
}
