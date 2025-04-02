namespace DADViewer.Models;

using System;
using System.IO;

public class DADData
{
    public int NWaves { get; private set; }
    public int NSpect { get; private set; }
    public double[] TimeStamps { get; private set; }
    public float[] Wavelengths { get; private set; }
    public double[,] Intensities { get; private set; }

    // Added Subsidiary properties
    public double[] TimeValues => TimeStamps;
    public int NumberOfSpectra => NSpect;
    public int NumberOfWavelengths => NWaves;

    // Subsidiary method for validating indexes
    public bool IsValidIndex(int timeIndex, int wavelengthIndex)
    {
        return timeIndex >= 0 && timeIndex < NSpect && 
               wavelengthIndex >= 0 && wavelengthIndex < NWaves;
    }

    public static DADData LoadFromFile(string filePath)
    {
        var dadData = new DADData();
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}");
        }
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            throw new FormatException("File is empty");
        }

        // Calculate the minimum file size (nWaves + nSpect + timeStamps + wavelengths + intensities)
        // 4 + 4 + (nSpect * 8) + (nWaves * 4) + (nSpect * nWaves * 8)
        const int headerSize = 8; // 4 bytes for nWaves + 4 bytes for nSpect

        using (var reader = new BinaryReader(File.Open(filePath, FileMode.Open)))
        {
            //read nWaves and nSpect
            dadData.NWaves = reader.ReadInt32();
            dadData.NSpect = reader.ReadInt32();
            
            if (dadData.NWaves <= 0 || dadData.NSpect <= 0)
            {
                throw new FormatException("Invalid data dimensions");
            }
            long expectedSize = headerSize + 
                              (dadData.NSpect * sizeof(double)) + // TimeStamps
                              (dadData.NWaves * sizeof(float)) +  // Wavelengths
                              (dadData.NSpect * dadData.NWaves * sizeof(double)); // Intensities

            if (fileInfo.Length < expectedSize)
            {
                throw new FormatException($"File is too small. Expected at least {expectedSize} bytes, got {fileInfo.Length} bytes");
            }
            
            // read table values (nSpect * 8 bytes)
            dadData.TimeStamps = new double[dadData.NSpect];
            for (int i = 0; i < dadData.NSpect; i++)
            {
                dadData.TimeStamps[i] = reader.ReadDouble();
            }
            
            //Read wave length (nWaves * 4 bytes)
            dadData.Wavelengths = new float[dadData.NWaves];
            for (int i = 0; i < dadData.NWaves; i++)
            {
                dadData.Wavelengths[i] = reader.ReadSingle();
            }
            
            // Read intensities matrix (nSpect * nWave * 8 bytes)
            dadData.Intensities = new double[dadData.NSpect, dadData.NWaves];
            for (int i = 0; i < dadData.NSpect; i++)
            {
                for (int j = 0; j < dadData.NWaves; j++)
                {
                    dadData.Intensities[i, j] = reader.ReadDouble();
                }
            }
        }
        return dadData;
    }
}