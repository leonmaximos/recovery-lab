namespace RecoveryLab;

internal sealed record NtfsTimes(DateTime? Created, DateTime? Modified, DateTime? MftModified, DateTime? Accessed);

internal sealed record NtfsFileName(string Name, byte NamespaceId, string Namespace, ulong ParentRecord,
    ushort ParentSequence, NtfsTimes Times, ulong AllocatedSize, ulong RealSize, uint Flags, uint ReparseOrEa);

internal sealed record NtfsStandardInformation(NtfsTimes Times, uint Flags);

internal sealed record NtfsAttribute(uint TypeId, string TypeName, uint Length, bool IsResident,
    string? Name, ushort Flags, ushort Id);

internal sealed record NtfsDataRun(ulong StartVcn, ulong ClusterCount, long? StartLcn);

internal sealed record NtfsDataStream(ushort Id, string? Name, ushort Flags, bool IsResident,
    ulong? DataSize, ulong? AllocatedSize, ulong? InitializedSize, ulong? CompressedSize,
    ulong? StartVcn, ulong? EndVcn, ushort? CompressionUnit, IReadOnlyList<NtfsDataRun> Runs);
