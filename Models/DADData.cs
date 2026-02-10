namespace DADViewer.Models;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public enum DADFileProfile
{
    Auto,
    StandardDoubleIntensity,
    StandardFloatIntensity,
    ReversedHeaderDoubleIntensity,
    ReversedHeaderFloatIntensity
}

public class DADData
{
    private const int HeaderSizeBytes = 8;
    private const long MaxDataPoints = 150_000_000;

    private static readonly ProfileDefinition[] AutoProfiles =
    {
        new(DADFileProfile.StandardDoubleIntensity, false, false),
        new(DADFileProfile.StandardFloatIntensity, false, true),
        new(DADFileProfile.ReversedHeaderDoubleIntensity, true, false),
        new(DADFileProfile.ReversedHeaderFloatIntensity, true, true)
    };

    public int NWaves { get; private set; }
    public int NSpect { get; private set; }
    public double[] TimeStamps { get; private set; } = Array.Empty<double>();
    public float[] Wavelengths { get; private set; } = Array.Empty<float>();
    public double[,] Intensities { get; private set; } = new double[0, 0];
    public DADFileProfile FileProfile { get; private set; } = DADFileProfile.Auto;

    public double[] TimeValues => TimeStamps;
    public int NumberOfSpectra => NSpect;
    public int NumberOfWavelengths => NWaves;

    public bool IsValidIndex(int timeIndex, int wavelengthIndex)
    {
        return timeIndex >= 0 && timeIndex < NSpect &&
               wavelengthIndex >= 0 && wavelengthIndex < NWaves;
    }

    public static DADData LoadFromFile(string filePath)
    {
        return LoadFromFile(filePath, DADFileProfile.Auto);
    }

    public static DADData LoadFromFile(string filePath, DADFileProfile profile)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            throw new FormatException("File is empty");
        }

        if (profile != DADFileProfile.Auto)
        {
            ProfileDefinition selected = GetProfile(profile);
            return ParseWithProfile(filePath, fileInfo, selected);
        }

        var candidates = new List<ParseCandidate>();
        var errors = new List<string>();

        foreach (ProfileDefinition candidateProfile in AutoProfiles)
        {
            try
            {
                DADData parsed = ParseWithProfile(filePath, fileInfo, candidateProfile);
                int score = ScoreCandidate(parsed);
                if (score >= 10)
                {
                    candidates.Add(new ParseCandidate(parsed, score));
                }
                else
                {
                    errors.Add($"{candidateProfile.Profile}: low confidence score {score}");
                }
            }
            catch (Exception ex) when (ex is FormatException or EndOfStreamException or OverflowException)
            {
                errors.Add($"{candidateProfile.Profile}: {ex.Message}");
            }
        }

        if (candidates.Count == 0)
        {
            throw new FormatException("Unsupported DAD binary format. Detection failed. " +
                                      string.Join(" | ", errors));
        }

        ParseCandidate best = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => Array.IndexOf(AutoProfiles, GetProfile(c.Data.FileProfile)))
            .First();

        return best.Data;
    }

    private static DADData ParseWithProfile(string filePath, FileInfo fileInfo, ProfileDefinition profile)
    {
        using var reader = new BinaryReader(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read));

        int first = reader.ReadInt32();
        int second = reader.ReadInt32();

        int nWaves = profile.IsHeaderReversed ? second : first;
        int nSpect = profile.IsHeaderReversed ? first : second;

        ValidateDimensions(nWaves, nSpect);

        int intensityElementSize = profile.UsesFloatIntensity ? sizeof(float) : sizeof(double);
        long expectedSize = checked(
            HeaderSizeBytes +
            ((long)nSpect * sizeof(double)) +
            ((long)nWaves * sizeof(float)) +
            ((long)nSpect * nWaves * intensityElementSize));

        if (fileInfo.Length < expectedSize)
        {
            throw new FormatException($"File is too small for profile {profile.Profile}. " +
                                      $"Expected at least {expectedSize} bytes, got {fileInfo.Length}");
        }

        var data = new DADData
        {
            NWaves = nWaves,
            NSpect = nSpect,
            FileProfile = profile.Profile,
            TimeStamps = new double[nSpect],
            Wavelengths = new float[nWaves],
            Intensities = new double[nSpect, nWaves]
        };

        for (int i = 0; i < nSpect; i++)
        {
            data.TimeStamps[i] = reader.ReadDouble();
        }

        for (int i = 0; i < nWaves; i++)
        {
            data.Wavelengths[i] = reader.ReadSingle();
        }

        for (int i = 0; i < nSpect; i++)
        {
            for (int j = 0; j < nWaves; j++)
            {
                data.Intensities[i, j] = profile.UsesFloatIntensity ? reader.ReadSingle() : reader.ReadDouble();
            }
        }

        return data;
    }

    private static ProfileDefinition GetProfile(DADFileProfile profile)
    {
        return profile switch
        {
            DADFileProfile.StandardDoubleIntensity => new ProfileDefinition(profile, false, false),
            DADFileProfile.StandardFloatIntensity => new ProfileDefinition(profile, false, true),
            DADFileProfile.ReversedHeaderDoubleIntensity => new ProfileDefinition(profile, true, false),
            DADFileProfile.ReversedHeaderFloatIntensity => new ProfileDefinition(profile, true, true),
            _ => throw new ArgumentException($"Profile '{profile}' is not a concrete parser profile.", nameof(profile))
        };
    }

    private static void ValidateDimensions(int nWaves, int nSpect)
    {
        if (nWaves <= 0 || nSpect <= 0)
        {
            throw new FormatException("Invalid data dimensions");
        }

        long dataPoints = (long)nWaves * nSpect;
        if (dataPoints > MaxDataPoints)
        {
            throw new FormatException($"Data size is too large ({dataPoints} points)");
        }
    }

    private static int ScoreCandidate(DADData data)
    {
        int score = 0;

        if (data.NWaves is >= 16 and <= 8192)
        {
            score += 2;
        }

        if (data.NSpect is >= 32 and <= 500_000)
        {
            score += 2;
        }

        double timeMonotonic = MonotonicRatio(data.TimeStamps);
        if (timeMonotonic >= 0.97)
        {
            score += 3;
        }
        else if (timeMonotonic >= 0.80)
        {
            score += 1;
        }

        double waveMonotonic = MonotonicRatio(data.Wavelengths);
        if (waveMonotonic >= 0.97)
        {
            score += 4;
        }
        else if (waveMonotonic >= 0.80)
        {
            score += 2;
        }

        float minWave = data.Wavelengths.Min();
        float maxWave = data.Wavelengths.Max();
        float waveRange = maxWave - minWave;
        if (waveRange > 10)
        {
            score += 1;
        }

        if (minWave >= 100 && maxWave <= 1200)
        {
            score += 4;
        }

        double minTime = data.TimeStamps.Min();
        double maxTime = data.TimeStamps.Max();
        if (maxTime > minTime)
        {
            score += 1;
        }

        if (maxTime - minTime < 1_000_000)
        {
            score += 1;
        }

        (double minIntensity, double maxIntensity, double finiteRatio) = GetSampledIntensityStats(data);
        if (finiteRatio > 0.98)
        {
            score += 2;
        }

        if (maxIntensity > minIntensity)
        {
            score += 1;
        }

        if (Math.Abs(maxIntensity) < 10_000_000 && Math.Abs(minIntensity) < 10_000_000)
        {
            score += 1;
        }

        return score;
    }

    private static (double min, double max, double finiteRatio) GetSampledIntensityStats(DADData data)
    {
        int rowStep = Math.Max(1, data.NSpect / 80);
        int colStep = Math.Max(1, data.NWaves / 80);

        double min = double.MaxValue;
        double max = double.MinValue;
        int finiteCount = 0;
        int sampledCount = 0;

        for (int i = 0; i < data.NSpect; i += rowStep)
        {
            for (int j = 0; j < data.NWaves; j += colStep)
            {
                sampledCount++;
                double value = data.Intensities[i, j];
                if (!double.IsFinite(value))
                {
                    continue;
                }

                finiteCount++;
                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }
            }
        }

        if (finiteCount == 0)
        {
            return (0, 0, 0);
        }

        double finiteRatio = sampledCount == 0 ? 0 : (double)finiteCount / sampledCount;
        return (min, max, finiteRatio);
    }

    private static double MonotonicRatio(double[] values)
    {
        if (values.Length < 2)
        {
            return 0;
        }

        int monotonic = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] >= values[i - 1])
            {
                monotonic++;
            }
        }

        return (double)monotonic / (values.Length - 1);
    }

    private static double MonotonicRatio(float[] values)
    {
        if (values.Length < 2)
        {
            return 0;
        }

        int monotonic = 0;
        for (int i = 1; i < values.Length; i++)
        {
            if (values[i] >= values[i - 1])
            {
                monotonic++;
            }
        }

        return (double)monotonic / (values.Length - 1);
    }

    private readonly record struct ProfileDefinition(
        DADFileProfile Profile,
        bool IsHeaderReversed,
        bool UsesFloatIntensity);

    private readonly record struct ParseCandidate(DADData Data, int Score);
}
