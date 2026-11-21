using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;

namespace RecoveryLab;

internal enum RecordFilter { All, InUse, Unused }
internal enum AnalysisState { Ready, Reading, Complete, Warnings, Failed }
internal sealed record FilterOption(string Label, RecordFilter Filter);
internal sealed record Metric(string Label, string Value, string Glyph, string Description);

internal sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly RelayCommand _analyze;
    private readonly RelayCommand _refresh;
    private VolumeOption? _selectedVolume;
    private RecordRow? _selectedRecord;
    private ListCollectionView _records = new(Array.Empty<RecordRow>());
    private string _search = "";
    private FilterOption _selectedFilter;
    private ScanResult? _result;
    private bool _isAnalyzing;
    private bool _isRefreshing;
    private bool _fatalError;
    private ScanProgress _progress = new(0, 0);
    private AnalysisState _state = AnalysisState.Ready;
    private string _statusDetail = "Aguardando a detecção dos volumes NTFS.";
    private string _volumeHint = "";

    internal MainViewModel()
    {
        _selectedFilter = Filters[0];
        _analyze = new RelayCommand(async () => await AnalyzeAsync(), () => SelectedVolume is not null && CanChooseVolume);
        _refresh = new RelayCommand(async () => await RefreshVolumesAsync(), () => !IsRefreshing && !IsAnalyzing);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<VolumeOption> Volumes { get; } = [];
    public IReadOnlyList<FilterOption> Filters { get; } =
    [
        new("Todos os status", RecordFilter.All),
        new("Em uso", RecordFilter.InUse),
        new("Não utilizados", RecordFilter.Unused)
    ];

    public ICommand AnalyzeCommand => _analyze;
    public ICommand RefreshVolumesCommand => _refresh;

    public VolumeOption? SelectedVolume
    {
        get => _selectedVolume;
        set
        {
            if (!Set(ref _selectedVolume, value))
                return;

            _fatalError = false;
            SetResult(null);
            SetStatus(AnalysisState.Ready, value is null
                ? "Selecione um volume NTFS disponível."
                : "Clique em Iniciar análise para examinar os registros da MFT.");
            _analyze.Invalidate();
        }
    }

    public RecordRow? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (Set(ref _selectedRecord, value))
                Notify(nameof(HasSelection), nameof(RecordDetails));
        }
    }

    public string Search
    {
        get => _search;
        set
        {
            if (Set(ref _search, value))
                RefreshRecords();
        }
    }

    public FilterOption SelectedFilter
    {
        get => _selectedFilter;
        set
        {
            if (value is not null && Set(ref _selectedFilter, value))
                RefreshRecords();
        }
    }

    public ListCollectionView Records => _records;
    public bool HasSelection => SelectedRecord is not null;
    public bool HasResult => _result is not null;
    public bool HasRows => Records.Count > 0;
    public string ResultCount => _result is null ? "" :
        $"{RecordRow.FormatNumber(Records.Count)} de {RecordRow.FormatNumber(_result.Records.Count)} registros";
    public string EmptyMessage => IsAnalyzing ? "Analisando os registros da MFT…" : _result switch
    {
        null => "Os registros aparecerão após a análise.",
        { Records.Count: 0 } => "A região examinada não contém registros válidos.",
        _ => "Nenhum registro corresponde à busca ou ao filtro."
    };
    public IReadOnlyList<DetailSection> RecordDetails => SelectedRecord?.GetDetails() ?? [];
    public IReadOnlyList<DetailSection> VolumeDetails => _result is null ? [] : RecordRow.GetVolumeDetails(_result);

    public bool IsAnalyzing => _isAnalyzing;
    public bool CanChooseVolume => !IsAnalyzing && !IsRefreshing;
    public bool IsRefreshing => _isRefreshing;
    public bool IsIndeterminate => _progress.Total == 0;
    public int ProgressMaximum => Math.Max(1, _progress.Total);
    public int ProgressValue => _progress.Examined;
    public AnalysisState State => _state;
    public string StatusDetail => _statusDetail;
    public string VolumeHint => _volumeHint;

    public string StatusTitle => State switch
    {
        AnalysisState.Reading => "Análise em andamento",
        AnalysisState.Complete => "Análise concluída",
        AnalysisState.Warnings => "Concluída com ocorrências",
        AnalysisState.Failed => "Falha na análise",
        _ => SelectedVolume is null ? "Seleção de volume" : "Pronto para analisar"
    };

    public IReadOnlyList<Metric> Metrics =>
    [
        new("Examinados", Count(_result?.Examined), "\uE9D9", "Slots examinados na região inicial da MFT."),
        new("Registros válidos", Count(_result?.Records.Count), "\uE73E", "Registros FILE aprovados na validação USA."),
        new("Arquivos em uso", Count(_result?.Records.Count(record => record.Header.InUse && !record.Header.IsDirectory)),
            "\uE8A5", "Arquivos em uso. Diretórios aparecem na tabela."),
        new("Não utilizados", Count(_result?.Records.Count(record => !record.Header.InUse)),
            "\uE74D", "Registros sem a marca de uso. Podem corresponder a exclusões."),
        new("Ocorrências", Count(_result?.Errors ?? (_fatalError ? 1 : null)),
            "\uE7BA", "Falhas de leitura ou validação, incluindo slots sem assinatura FILE.")
    ];

    private async Task AnalyzeAsync()
    {
        if (SelectedVolume is not VolumeOption volume || !CanChooseVolume)
            return;

        _isAnalyzing = true;
        _fatalError = false;
        _progress = new ScanProgress(0, 0);
        SetResult(null);
        SetStatus(AnalysisState.Reading, "Lendo o setor de inicialização e a região inicial da MFT…");
        NotifyOperation();
        var progress = new Progress<ScanProgress>(update =>
        {
            if (!IsAnalyzing)
                return;

            _progress = update;
            SetStatus(AnalysisState.Reading,
                $"Validando registros · {RecordRow.FormatNumber(update.Examined)} de {RecordRow.FormatNumber(update.Total)}");
            Notify(nameof(IsIndeterminate), nameof(ProgressMaximum), nameof(ProgressValue));
        });

        try
        {
            var result = await Task.Run(() => MftAnalysisService.Analyze(volume.ReadBytes, progress));
            SetResult(result);
            SelectedRecord = Records.OfType<RecordRow>().FirstOrDefault();
            SetStatus(result.Errors == 0 ? AnalysisState.Complete : AnalysisState.Warnings, result.Errors == 0
                ? "Região inicial da MFT examinada."
                : "Consulte as ocorrências na aba Volume e os avisos nos registros afetados.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception
            or InvalidDataException or OverflowException)
        {
            _fatalError = true;
            Notify(nameof(Metrics));
            SetStatus(AnalysisState.Failed, ErrorMessage(ex));
        }
        finally
        {
            _isAnalyzing = false;
            NotifyOperation();
        }
    }

    internal async Task RefreshVolumesAsync()
    {
        if (IsRefreshing || IsAnalyzing)
            return;

        Set(ref _isRefreshing, true, nameof(IsRefreshing));
        NotifyOperation();
        Set(ref _volumeHint, "Procurando volumes NTFS…", nameof(VolumeHint));
        if (SelectedVolume is null)
            SetStatus(AnalysisState.Ready, VolumeHint);

        try
        {
            char? previousLetter = SelectedVolume?.Letter;
            var detected = await Task.Run(VolumeCatalog.GetNtfsVolumes);
            foreach (var volume in Volumes.Where(volume => !detected.Contains(volume)).ToArray())
                Volumes.Remove(volume);
            foreach (var volume in detected.Where(volume => !Volumes.Contains(volume)))
                Volumes.Add(volume);

            SelectedVolume = Volumes.FirstOrDefault(volume => volume.Letter == previousLetter)
                ?? Volumes.FirstOrDefault();
            Set(ref _volumeHint, detected.Count == 0 ? "Nenhum volume NTFS disponível foi detectado."
                : "A análise cobre a região inicial da MFT.", nameof(VolumeHint));
            if (SelectedVolume is null)
                SetStatus(AnalysisState.Ready, VolumeHint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            Set(ref _volumeHint, $"Falha ao listar os volumes. {ex.Message}", nameof(VolumeHint));
            if (SelectedVolume is null)
                SetStatus(AnalysisState.Ready, VolumeHint);
        }
        finally
        {
            Set(ref _isRefreshing, false, nameof(IsRefreshing));
            NotifyOperation();
        }
    }

    private void SetResult(ScanResult? result)
    {
        SelectedRecord = null;
        _result = result;
        var rows = result?.Records.Select(entry => new RecordRow(entry)).ToArray() ?? [];
        _records = new ListCollectionView(rows) { Filter = Matches };
        Notify(nameof(Records), nameof(HasResult), nameof(HasRows), nameof(EmptyMessage),
            nameof(Metrics), nameof(ResultCount), nameof(VolumeDetails));
    }

    private bool Matches(object item) => item is RecordRow row && row.MatchesName(Search.Trim()) &&
        SelectedFilter.Filter switch
        {
            RecordFilter.InUse => row.InUse,
            RecordFilter.Unused => !row.InUse,
            _ => true
        };

    private void RefreshRecords()
    {
        Records.Refresh();
        if (SelectedRecord is not null && !Records.Contains(SelectedRecord))
            SelectedRecord = null;

        Notify(nameof(HasRows), nameof(ResultCount), nameof(EmptyMessage));
    }

    private void SetStatus(AnalysisState state, string detail)
    {
        _state = state;
        _statusDetail = detail;
        Notify(nameof(State), nameof(StatusTitle), nameof(StatusDetail));
    }

    private void NotifyOperation()
    {
        Notify(nameof(IsAnalyzing), nameof(CanChooseVolume), nameof(EmptyMessage),
            nameof(IsIndeterminate), nameof(ProgressMaximum), nameof(ProgressValue));
        _analyze.Invalidate();
        _refresh.Invalidate();
    }

    private static string Count(int? value) => value is int count ? RecordRow.FormatNumber(count) : "—";

    private static string ErrorMessage(Exception exception) => exception switch
    {
        UnauthorizedAccessException or Win32Exception { NativeErrorCode: 5 or 1314 } =>
            "Acesso negado. Selecione um volume acessível ou abra o RecoveryLab como administrador para analisá-lo.",
        EndOfStreamException => "O volume retornou menos dados do que o esperado. Verifique se ele continua disponível.",
        InvalidDataException => exception.Message,
        OverflowException => "A geometria do volume excede os limites aceitos pela análise.",
        _ => $"Não foi possível ler o volume. {exception.Message}"
    };

    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        Notify(name);
        return true;
    }

    private void Notify(params string[] names)
    {
        foreach (string name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

internal sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => execute();
    internal void Invalidate() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

