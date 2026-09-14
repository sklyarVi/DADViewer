using System;
using System.IO;

namespace DADViewer.Samples
{
    // MIT-licensed synthetic fixtures. No measured data from the original assignment.
    // Shared by tests and GenerateSamples.ps1 to keep clean checkouts self-contained.
    public static class SyntheticSamples
    {
        public static void WriteAll(string directory)
        {
            Directory.CreateDirectory(directory);
            int[] counts = { 4869, 3294, 4494 };
            for (int sample = 0; sample < counts.Length; sample++)
            {
                using (var writer = new BinaryWriter(File.Create(Path.Combine(directory, "TestData" + (sample + 1) + ".DAD"))))
                {
                    const int waves = 106;
                    int spectra = counts[sample];
                    writer.Write(waves); writer.Write(spectra);
                    for (int t = 0; t < spectra; t++) writer.Write(t / 150.0);
                    for (int w = 0; w < waves; w++) writer.Write((float)(190 + 2 * w));
                    for (int t = 0; t < spectra; t++)
                    {
                        double time = t / 150.0;
                        for (int w = 0; w < waves; w++)
                        {
                            double wavelength = 190 + 2 * w;
                            double signal = -0.025 + 0.004 * Math.Sin(time + w * 0.11);
                            signal += Peak(time, wavelength, 4.2 + sample * 0.3, 225, 0.12, 10, 0.8);
                            signal += Peak(time, wavelength, 10.2, 276, 0.22, 15, 2.0 + sample * 0.2);
                            signal += Peak(time, wavelength, 16.0 - sample * 0.2, 330, 0.08, 22, 1.3);
                            writer.Write(signal);
                        }
                    }
                }
            }
        }
        private static double Peak(double t, double w, double centerT, double centerW, double widthT, double widthW, double height)
        {
            double x = (t - centerT) / widthT, y = (w - centerW) / widthW;
            return height * Math.Exp(-0.5 * (x * x + y * y));
        }
    }
}
