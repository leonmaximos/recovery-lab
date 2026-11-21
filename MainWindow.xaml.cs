using System.Windows;

namespace RecoveryLab;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.SelectedRecord))
                RecordInspectorScroll.ScrollToTop();
            if (args.PropertyName == nameof(MainViewModel.VolumeDetails))
                VolumeInspectorScroll.ScrollToTop();
        };
        Loaded += LoadVolumesAsync;
    }

    private async void LoadVolumesAsync(object sender, RoutedEventArgs e)
    {
        Loaded -= LoadVolumesAsync;
        await _viewModel.RefreshVolumesAsync();
    }
}
