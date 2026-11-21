using System.IO;

namespace RecoveryLab;

internal sealed record MftEntry(int Number, MftHeader Header, IReadOnlyList<NtfsFileName> Names,
    NtfsStandardInformation? StandardInformation, IReadOnlyList<NtfsAttribute> Attributes,
    IReadOnlyList<NtfsDataStream> Streams, IReadOnlyList<string> Warnings)
{
    public NtfsFileName? PrimaryName => Names.FirstOrDefault(name => name.NamespaceId is 1 or 3)
        ?? Names.FirstOrDefault(name => name.NamespaceId != 2) ?? Names.FirstOrDefault();

    public NtfsDataStream? MainStream => Streams.FirstOrDefault(stream => stream.Name is null
        && (stream.IsResident || stream.StartVcn == 0));
}

internal sealed record ScanProgress(int Examined, int Total);

internal sealed record ScanResult(NtfsVolume Volume, int Examined, IReadOnlyList<MftEntry> Records,
    int InvalidSignatures, int FixupFailures, int AttributeFailures, IReadOnlyList<string> FixupExamples)
{
    public int Errors => InvalidSignatures + FixupFailures + AttributeFailures;
}

internal static class MftAnalysisService
{
    internal static ScanResult Analyze(Func<long, int, byte[]> readBytes, IProgress<ScanProgress>? progress = null)
    {
        var region = MftScan.ReadRegion(readBytes);
        List<MftEntry> records = new();
        List<string> fixupExamples = new();
        int invalidSignatures = 0;
        int fixupFailures = 0;
        int attributeFailures = 0;
        progress?.Report(new ScanProgress(0, region.RecordCount));

        foreach (var slot in MftScan.Inspect(region))
        {
            if (slot.FixupError is string fixupError)
            {
                fixupFailures++;
                if (fixupExamples.Count < 3)
                    fixupExamples.Add($"Registro {slot.Number} · {fixupError}");
            }
            else if (slot.Record is byte[] record && slot.Header is MftHeader header)
            {
                var entry = ReadEntry(slot.Number, header, record);
                records.Add(entry);
                if (entry.Warnings.Count > 0)
                    attributeFailures++;
            }
            else
            {
                invalidSignatures++;
            }

            int examined = slot.Number + 1;
            if (examined % 16 == 0 || examined == region.RecordCount)
                progress?.Report(new ScanProgress(examined, region.RecordCount));
        }

        return new ScanResult(region.Volume, region.RecordCount, records, invalidSignatures,
            fixupFailures, attributeFailures, fixupExamples);
    }

    private static MftEntry ReadEntry(int number, MftHeader header, byte[] record)
    {
        List<string> warnings = new();
        var attributes = ReadSection("Atributos NTFS", MftRecordParser.ReadAttributes, record, [], warnings);
        var names = ReadSection("$FILE_NAME", MftRecordParser.ReadFileNames, record, [], warnings);
        var standard = ReadSection("$STANDARD_INFORMATION", MftRecordParser.ReadStandardInformation,
            record, null, warnings);
        var streams = ReadSection("$DATA", MftRecordParser.ReadDataStreams, record, [], warnings);
        return new MftEntry(number, header, names, standard, attributes, streams, warnings);
    }

    private static T ReadSection<T>(string section, Func<byte[], T> read, byte[] record, T fallback,
        List<string> warnings)
    {
        try
        {
            return read(record);
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        {
            warnings.Add($"{section} · {ex.Message}");
            return fallback;
        }
    }
}
