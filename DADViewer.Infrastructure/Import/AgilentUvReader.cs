using DADViewer.Domain;
namespace DADViewer.Infrastructure.Import;

public sealed class AgilentUvReader
{
    public DADData Read(string path, CancellationToken token = default, IProgress<double>? progress = null)
    {
        using var stream = ImportGuard.Open(path); using var reader = new BinaryReader(stream);
        try
        {
            token.ThrowIfCancellationRequested(); string version = ImportGuard.Pascal(reader, 0, false);
            bool modern = version == "131";
            if (!modern && version != "31") throw new FormatException("Unsupported Agilent UV version; supported: 31 and 131.");
            int start = modern ? 0x1000 : 0x200;
            if (stream.Length < start + 22) throw new FormatException("Agilent UV header is incomplete.");
            string type = modern ? ImportGuard.Pascal(reader, 0x15B, true) : "LC";
            bool array = type.StartsWith("OL", StringComparison.Ordinal);
            if (!array && !type.StartsWith("LC", StringComparison.Ordinal)) throw new FormatException("Unsupported Agilent UV encoding.");
            stream.Position = 0x116; uint count = ImportGuard.BigUInt(reader);
            bool unfinalized = count == 0; bool incompleteTail = false;
            stream.Position = modern ? 0xC0D : 0x13E; double scale = ImportGuard.BigDouble(reader);
            if (!double.IsFinite(scale) || scale <= 0) throw new FormatException("Invalid Agilent intensity scaling factor.");
            string originalUnit = ImportGuard.Pascal(reader, modern ? 0xC15 : 0x146, modern);
            var unit = ImportGuard.Unit(originalUnit);
            stream.Position = start + 8; ushort low = reader.ReadUInt16(), high = reader.ReadUInt16(), step = reader.ReadUInt16();
            if (low == 0 || step == 0 || high < low || (high - low) % step != 0) throw new FormatException("Invalid Agilent wavelength grid.");
            int columns = (high - low) / step + 1;
            if (unfinalized)
            {
                // A zero count occurs in unfinalized acquisitions. Recover only whole,
                // structurally valid segments and report that provenance explicitly.
                stream.Position = start;
                while (stream.Position < stream.Length)
                {
                    token.ThrowIfCancellationRequested(); long position = stream.Position;
                    if (stream.Length - position < 22)
                    {
                        var tail = reader.ReadBytes((int)(stream.Length - position));
                        if (tail.Length > 4 || tail.Any(b => b != 0)) incompleteTail = true;
                        break;
                    }
                    ushort label = reader.ReadUInt16(), length = reader.ReadUInt16();
                    if (label != (array ? 70 : 67) || length < 22) throw new FormatException("Invalid segment in unfinalized Agilent UV acquisition.");
                    if (position + length > stream.Length) { incompleteTail = true; break; }
                    count++; ImportGuard.Dimensions(count, columns); stream.Position = position + length;
                }
                if (count == 0) throw new FormatException("Unfinalized Agilent UV file contains no complete spectra.");
            }
            ImportGuard.Dimensions(count, columns);
            if ((long)count * (22 + columns * (array ? 8L : 2L)) > stream.Length - start) throw new FormatException("Agilent UV spectra are truncated.");
            var times = new double[count]; var waves = Enumerable.Range(0, columns).Select(i => (low + i * step) / 20d).ToArray();
            var values = new double[count, columns]; stream.Position = start;
            for (int t = 0; t < count; t++)
            {
                token.ThrowIfCancellationRequested(); long position = stream.Position;
                ushort label = reader.ReadUInt16(), length = reader.ReadUInt16();
                if (label != (array ? 70 : 67) || length < 22 || position + length > stream.Length) throw new FormatException($"Invalid Agilent spectrum segment {t}.");
                times[t] = reader.ReadUInt32() / 60000d;
                if (reader.ReadUInt16() != low || reader.ReadUInt16() != high || reader.ReadUInt16() != step)
                    throw new FormatException("Changing Agilent wavelength grids are not supported; no resampling was applied.");
                stream.Position += 8; long accumulator = 0;
                for (int w = 0; w < columns; w++)
                {
                    if ((w & 1023) == 0) token.ThrowIfCancellationRequested();
                    double raw;
                    if (array) raw = reader.ReadDouble();
                    else { short delta = reader.ReadInt16(); accumulator = delta == short.MinValue ? reader.ReadInt32() : accumulator + delta; raw = accumulator; }
                    values[t, w] = raw * scale * unit.Factor;
                }
                if (stream.Position != position + length) throw new FormatException($"Agilent spectrum {t} length does not match its wavelength data.");
                progress?.Report((t + 1d) * 90 / count);
            }
            var metadata = new DataMetadata($"Agilent UV {version} ({(array ? "OL" : "LC")}{(unfinalized ? ", unfinalized" : "")})", unit.Unit, $"Header unit {originalUnit}; file scaling applied" + (unfinalized ? $". Warning: unfinalized acquisition; {count} complete spectra recovered" + (incompleteTail ? "; incomplete trailing spectrum omitted" : "") : ""), Path.GetFullPath(path), ImportGuard.Fingerprint(path));
            var data = ImportGuard.Finish(times, waves, values, metadata, token); progress?.Report(100); return data;
        }
        catch (EndOfStreamException ex) { throw new FormatException("Agilent UV file is truncated.", ex); }
    }
}
