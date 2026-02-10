using System;
using System.IO;
using DADViewer.Models;
using Xunit;

namespace DADViewer.Tests;

public class DADDataTests
{
    [Fact]
    public void LoadFromFile_ValidSampleFile_ReturnsExpectedDimensions()
    {
        string samplePath = Path.Combine(GetRepositoryRoot(), "SampleTestData", "TestData1.DAD");

        DADData data = DADData.LoadFromFile(samplePath);

        Assert.Equal(106, data.NWaves);
        Assert.Equal(4869, data.NSpect);
        Assert.Equal(DADFileProfile.StandardDoubleIntensity, data.FileProfile);
        Assert.Equal(data.NSpect, data.TimeStamps.Length);
        Assert.Equal(data.NWaves, data.Wavelengths.Length);
        Assert.Equal(data.NSpect, data.Intensities.GetLength(0));
        Assert.Equal(data.NWaves, data.Intensities.GetLength(1));
    }

    [Fact]
    public void LoadFromFile_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => DADData.LoadFromFile("missing_file.dad"));
    }

    [Fact]
    public void LoadFromFile_EmptyFile_ThrowsFormatException()
    {
        string file = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(file, Array.Empty<byte>());
            Assert.Throws<FormatException>(() => DADData.LoadFromFile(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadFromFile_InvalidDimensions_ThrowsFormatException()
    {
        string file = Path.GetTempFileName();
        try
        {
            using (var writer = new BinaryWriter(File.Open(file, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                writer.Write(0);
                writer.Write(0);
            }

            Assert.Throws<FormatException>(() => DADData.LoadFromFile(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadFromFile_TruncatedFile_ThrowsFormatException()
    {
        string file = Path.GetTempFileName();
        try
        {
            using (var writer = new BinaryWriter(File.Open(file, FileMode.Create, FileAccess.Write, FileShare.None)))
            {
                writer.Write(10);
                writer.Write(10);
                writer.Write(1.0);
            }

            Assert.Throws<FormatException>(() => DADData.LoadFromFile(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadFromFile_SyntheticDoubleData_RoundTripsExpectedValues()
    {
        string file = Path.GetTempFileName();
        try
        {
            WriteSyntheticDAD(file, reversedHeader: false, floatIntensity: false);

            DADData data = DADData.LoadFromFile(file);

            Assert.Equal(DADFileProfile.StandardDoubleIntensity, data.FileProfile);
            AssertSyntheticValues(data);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadFromFile_SyntheticFloatIntensity_AutoDetectsAndParses()
    {
        string file = Path.GetTempFileName();
        try
        {
            WriteSyntheticDAD(file, reversedHeader: false, floatIntensity: true);

            DADData data = DADData.LoadFromFile(file);

            Assert.Equal(DADFileProfile.StandardFloatIntensity, data.FileProfile);
            AssertSyntheticValues(data);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void LoadFromFile_SyntheticReversedHeader_AutoDetectsAndParses()
    {
        string file = Path.GetTempFileName();
        try
        {
            WriteSyntheticDAD(file, reversedHeader: true, floatIntensity: false);

            DADData data = DADData.LoadFromFile(file);

            Assert.Equal(DADFileProfile.ReversedHeaderDoubleIntensity, data.FileProfile);
            AssertSyntheticValues(data);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static void AssertSyntheticValues(DADData data)
    {
        Assert.Equal(3, data.NWaves);
        Assert.Equal(2, data.NSpect);
        Assert.Equal(new double[] { 1.5, 2.5 }, data.TimeStamps);
        Assert.Equal(new float[] { 210f, 220f, 230f }, data.Wavelengths);
        Assert.Equal(11, data.Intensities[0, 0], 4);
        Assert.Equal(12, data.Intensities[0, 1], 4);
        Assert.Equal(13, data.Intensities[0, 2], 4);
        Assert.Equal(21, data.Intensities[1, 0], 4);
        Assert.Equal(22, data.Intensities[1, 1], 4);
        Assert.Equal(23, data.Intensities[1, 2], 4);
    }

    private static void WriteSyntheticDAD(string path, bool reversedHeader, bool floatIntensity)
    {
        const int nWaves = 3;
        const int nSpect = 2;

        using var writer = new BinaryWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None));

        if (reversedHeader)
        {
            writer.Write(nSpect);
            writer.Write(nWaves);
        }
        else
        {
            writer.Write(nWaves);
            writer.Write(nSpect);
        }

        writer.Write(1.5);
        writer.Write(2.5);

        writer.Write(210f);
        writer.Write(220f);
        writer.Write(230f);

        WriteIntensity(writer, 11, floatIntensity);
        WriteIntensity(writer, 12, floatIntensity);
        WriteIntensity(writer, 13, floatIntensity);
        WriteIntensity(writer, 21, floatIntensity);
        WriteIntensity(writer, 22, floatIntensity);
        WriteIntensity(writer, 23, floatIntensity);
    }

    private static void WriteIntensity(BinaryWriter writer, double value, bool floatIntensity)
    {
        if (floatIntensity)
        {
            writer.Write((float)value);
        }
        else
        {
            writer.Write(value);
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DADViewer.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
