using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace RecoveryLab;

internal sealed record NtfsVolume(string OemId, ushort BootSignature, ulong TotalSectors,
    int BytesPerSector, int SectorsPerCluster, long ClusterSize, int RecordSize,
    ulong MftStartCluster, ulong MftMirrorCluster, long MftOffset);

internal sealed record MftRegion(NtfsVolume Volume, byte[] Bytes)
{
    public int RecordCount => Bytes.Length / Volume.RecordSize;
}

internal sealed record MftHeader(ushort Sequence, ushort HardLinks, ushort Flags, uint UsedSize, uint AllocatedSize)
{
    public bool InUse => (Flags & 0x0001) != 0;
    public bool IsDirectory => (Flags & 0x0002) != 0;
}

internal sealed record MftSlot(int Number, byte[]? Record, MftHeader? Header, string? FixupError);

internal static class MftScan
{
    internal const int ClusterCount = 64;

    internal static MftRegion ReadRegion(Func<long, int, byte[]> readBytes)
    {
        byte[] bootSector = readBytes(0, 512);
        if (bootSector.Length != 512)
            throw new EndOfStreamException();

        string oemId = Encoding.ASCII.GetString(bootSector, 3, 8);
        ushort signature = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.AsSpan(510, 2));
        if (oemId != "NTFS    " || signature != 0xAA55)
            throw new InvalidDataException("O setor de inicialização não contém uma assinatura NTFS válida.");

        ulong totalSectors = BinaryPrimitives.ReadUInt64LittleEndian(bootSector.AsSpan(40, 8));
        ulong mftStartCluster = BinaryPrimitives.ReadUInt64LittleEndian(bootSector.AsSpan(48, 8));
        ulong mftMirrorCluster = BinaryPrimitives.ReadUInt64LittleEndian(bootSector.AsSpan(56, 8));
        sbyte clustersPerRecord = unchecked((sbyte)bootSector[64]);
        int bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(bootSector.AsSpan(11, 2));
        int sectorsPerCluster = bootSector[13];
        if (bytesPerSector < 512 || sectorsPerCluster == 0 || clustersPerRecord is 0 or < -31)
            throw new InvalidDataException("A geometria NTFS do setor de inicialização é inválida.");

        long clusterSize = (long)bytesPerSector * sectorsPerCluster;
        long recordSize = clustersPerRecord > 0 ? clustersPerRecord * clusterSize : 1L << -clustersPerRecord;
        if (recordSize < 48 || recordSize > 1024 * 1024 || recordSize % bytesPerSector != 0)
            throw new InvalidDataException("O tamanho do registro MFT é inválido ou incompatível.");

        long mftOffset = checked((long)(mftStartCluster * (ulong)clusterSize));
        int length = checked((int)(ClusterCount * clusterSize));
        var volume = new NtfsVolume(oemId.TrimEnd(), signature, totalSectors, bytesPerSector,
            sectorsPerCluster, clusterSize, (int)recordSize, mftStartCluster, mftMirrorCluster, mftOffset);
        byte[] mft = readBytes(mftOffset, length);
        if (mft.Length != length)
            throw new EndOfStreamException();

        return new MftRegion(volume, mft);
    }

    internal static IEnumerable<MftSlot> Inspect(MftRegion region)
    {
        int recordSize = region.Volume.RecordSize;
        for (int i = 0; i < region.RecordCount; i++)
        {
            byte[] currentRecord = new byte[recordSize];
            Buffer.BlockCopy(region.Bytes, i * recordSize, currentRecord, 0, recordSize);
            if (!currentRecord.AsSpan(0, 4).SequenceEqual("FILE"u8))
            {
                yield return new MftSlot(i, null, null, null);
                continue;
            }

            byte[]? correctedRecord = null;
            string? fixupError = null;
            try
            {
                correctedRecord = MftRecordParser.ApplyUsaFixups(currentRecord, region.Volume.BytesPerSector);
            }
            catch (InvalidDataException ex)
            {
                fixupError = ex.Message;
            }

            MftHeader? header = correctedRecord is null ? null : ReadHeader(correctedRecord);
            yield return new MftSlot(i, correctedRecord, header, fixupError);
        }
    }

    private static MftHeader ReadHeader(byte[] record) => new(
        BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(16, 2)),
        BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(18, 2)),
        BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(22, 2)),
        BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(24, 4)),
        BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(28, 4)));
}
