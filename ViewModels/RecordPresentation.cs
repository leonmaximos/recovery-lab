using System.Globalization;

namespace RecoveryLab;

internal sealed record DetailField(string Label, string Value);
internal sealed record DetailSection(string Title, IReadOnlyList<DetailField> Fields);

internal sealed class RecordRow(MftEntry entry)
{
    internal MftEntry Entry { get; } = entry;
    public int Number => Entry.Number;
    public bool InUse => Entry.Header.InUse;
    public bool IsDirectory => Entry.Header.IsDirectory;
    public string Name => Entry.PrimaryName?.Name ?? "(sem nome)";
    public string Status => InUse ? "Em uso" : "Não utilizado";
    public string Type => IsDirectory ? "Diretório" : "Arquivo";
    public ulong? Size => IsDirectory ? null : Entry.MainStream?.DataSize ?? Entry.PrimaryName?.RealSize;
    public string SizeText => FormatBytes(Size);
    private NtfsTimes? Times => Entry.StandardInformation?.Times ?? Entry.PrimaryName?.Times;
    public DateTime? Modified => Times?.Modified;
    public string ModifiedText => Modified?.ToString("dd/MM/yyyy HH:mm", Portuguese) ?? "—";
    public string DataSummary { get; } = FormatDataSummary(entry.Attributes);

    private static string FormatDataSummary(IReadOnlyList<NtfsAttribute> attributes)
    {
        var data = attributes.Where(attribute => attribute.TypeId == 0x80)
            .Select(attribute => attribute.IsResident).Distinct().ToArray();
        return data.Length == 2 ? "Misto" : data.Length == 1 ? Residency(data[0]) : "—";
    }

    internal bool MatchesName(string search) => search.Length == 0 ||
        Entry.Names.Any(name => Portuguese.CompareInfo.IndexOf(name.Name, search,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0);

    internal IReadOnlyList<DetailSection> GetDetails()
    {
        var header = Entry.Header;
        var name = Entry.PrimaryName;
        var details = new List<DetailSection>
        {
            new("Registro MFT",
            [
                new("Número do registro", FormatNumber(Number)),
                new("Sequência", FormatNumber(header.Sequence)),
                new("Diretório pai", name is null ? "—" : $"#{FormatNumber(name.ParentRecord)} · seq. {name.ParentSequence}"),
                new("Vínculos físicos", FormatNumber(header.HardLinks)),
                new("Flags do registro", $"0x{header.Flags:X4}"),
                new("Tamanho lógico do arquivo", SizeText),
                new("Tamanho alocado do arquivo",
                    FormatBytes(IsDirectory ? null : Entry.MainStream?.AllocatedSize ?? name?.AllocatedSize)),
                new("Espaço usado no registro", FormatBytes(header.UsedSize)),
                new("Espaço alocado ao registro", FormatBytes(header.AllocatedSize))
            ])
        };

        if (!InUse)
            details.Insert(0, new("Estado do registro",
                [new("Possível exclusão", "O registro não está em uso. A possibilidade de recuperação é desconhecida.")]));

        if (Entry.Warnings.Count > 0)
            details.Insert(0, new("Metadados incompletos",
                Entry.Warnings.Select(warning => new DetailField("Falha de interpretação", warning)).ToArray()));

        if (Times is NtfsTimes times)
        {
            uint flags = Entry.StandardInformation?.Flags ?? name?.Flags ?? 0;
            details.Add(new("Datas e atributos do arquivo",
            [
                new("Origem", Entry.StandardInformation is null ? "$FILE_NAME" : "$STANDARD_INFORMATION"),
                new("Criação (UTC)", FormatTime(times.Created)),
                new("Modificação (UTC)", FormatTime(times.Modified)),
                new("Alteração na MFT (UTC)", FormatTime(times.MftModified)),
                new("Último acesso (UTC)", FormatTime(times.Accessed)),
                new("Flags do arquivo", $"0x{flags:X8}")
            ]));
        }

        if (Entry.Names.Count > 0)
        {
            details.Add(new("Nomes no NTFS", Entry.Names.Select(item => new DetailField(item.Namespace,
                $"{item.Name} · pai #{item.ParentRecord} · seq. {item.ParentSequence}")).ToArray()));
        }

        foreach (var stream in Entry.Streams)
        {
            var fields = new List<DetailField>
            {
                new("Residência", Residency(stream.IsResident)),
                new("ID e flags", $"#{stream.Id} · 0x{stream.Flags:X4}"),
                new("Tamanho lógico", FormatBytes(stream.DataSize))
            };

            if (!stream.IsResident)
            {
                fields.AddRange(
                [
                    new("Tamanho alocado", FormatBytes(stream.AllocatedSize)),
                    new("Tamanho inicializado", FormatBytes(stream.InitializedSize)),
                    new("Tamanho comprimido/alocado", FormatBytes(stream.CompressedSize)),
                    new("Intervalo de VCN", $"{stream.StartVcn}–{stream.EndVcn}"),
                    new("Unidade de compressão", $"{stream.CompressionUnit}"),
                    new("Data runs", FormatNumber(stream.Runs.Count))
                ]);

                fields.AddRange(stream.Runs.Select((run, index) => new DetailField(
                    $"Trecho {index + 1} · {FormatNumber(run.ClusterCount)} clusters",
                    $"VCN {FormatNumber(run.StartVcn)} · " +
                    (run.StartLcn is long lcn ? $"LCN {FormatNumber(lcn)}" : "esparso"))));
            }

            details.Add(new($"$DATA · {stream.Name ?? "sem nome"}", fields));
        }

        if (Entry.Attributes.Count > 0)
        {
            details.Add(new("Atributos NTFS", Entry.Attributes.Select(attribute => new DetailField(
                $"{attribute.TypeName}{(attribute.Name is not null ? $" · {attribute.Name}" : "")}",
                $"ID {attribute.Id} · 0x{attribute.TypeId:X} · {Residency(attribute.IsResident)} · " +
                $"{FormatBytes(attribute.Length)} · flags 0x{attribute.Flags:X4}")).ToArray()));
        }

        return details;
    }

    internal static IReadOnlyList<DetailSection> GetVolumeDetails(ScanResult result)
    {
        var volume = result.Volume;
        List<DetailSection> details =
        [
            new("Setor de inicialização",
            [
                new("Sistema de arquivos", volume.OemId),
                new("Assinatura", $"0x{volume.BootSignature:X4}"),
                new("Total de setores", FormatNumber(volume.TotalSectors)),
                new("Bytes por setor", FormatNumber(volume.BytesPerSector)),
                new("Setores por cluster", FormatNumber(volume.SectorsPerCluster)),
                new("Tamanho do cluster", FormatBytes((ulong)volume.ClusterSize))
            ]),
            new("Região da MFT",
            [
                new("Cluster inicial da MFT", FormatNumber(volume.MftStartCluster)),
                new("Cluster do espelho da MFT", FormatNumber(volume.MftMirrorCluster)),
                new("Deslocamento inicial", FormatBytes((ulong)volume.MftOffset)),
                new("Tamanho de um registro", FormatBytes((ulong)volume.RecordSize)),
                new("Clusters examinados", FormatNumber(MftScan.ClusterCount))
            ]),
            new("Ocorrências",
            [
                new("Slots sem assinatura FILE", FormatNumber(result.InvalidSignatures)),
                new("Falhas na validação USA", FormatNumber(result.FixupFailures)),
                new("Registros com atributos inválidos", FormatNumber(result.AttributeFailures))
            ])
        ];

        if (result.FixupExamples.Count > 0)
            details.Add(new("Exemplos de falhas USA", result.FixupExamples
                .Select(error => new DetailField("Falha de validação", error)).ToArray()));

        return details;
    }

    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");

    private static string Residency(bool resident) => resident ? "Residente" : "Não residente";
    private static string FormatTime(DateTime? value) => value?.ToString("dd/MM/yyyy HH:mm:ss", Portuguese) ?? "—";
    private static string FormatBytes(ulong? bytes) => bytes is ulong size ? $"{FormatNumber(size)} B" : "—";
    internal static string FormatNumber<T>(T number) where T : IFormattable => number.ToString("N0", Portuguese);
}
