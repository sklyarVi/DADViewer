namespace DADViewer.Domain;

public sealed record DataMetadata(string Format = "Assignment DAD", string IntensityUnit = "file units", string Detail = "", string? ResolvedPath = null, string Fingerprint = "");
