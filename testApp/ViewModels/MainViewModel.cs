using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace testApp.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // Assigned by the View once it has access to the TopLevel (needed for the file picker).
    public IStorageProvider? StorageProvider { get; set; }

    private long _originalFileSizeBytes;

    [ObservableProperty]
    private string _fileName = "Aucune vidéo sélectionnée";

    [ObservableProperty]
    private string _fileSize = "0 MB";

    [ObservableProperty]
    private int _compressionLevel = 25; // CRF value: lower = better quality / bigger file

    [ObservableProperty]
    private string _selectedResolution = "1080p";

    [ObservableProperty]
    private string _estimatedSize = "~0 MB";

    [ObservableProperty]
    private string _estimatedReduction = "-0 MB";

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressPercent = "0%";

    [ObservableProperty]
    private string _countdownText = "--:--";

    [ObservableProperty]
    private bool _isProcessing;

    public bool HasFile => _originalFileSizeBytes > 0;

    partial void OnCompressionLevelChanged(int value) => RecalculateEstimate();

    [RelayCommand]
    private async Task PickVideoAsync()
    {
        if (StorageProvider is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir une vidéo",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Vidéos")
                {
                    Patterns = new[] { "*.mp4", "*.mov", "*.mkv", "*.avi", "*.webm" }
                }
            }
        });

        var file = files.FirstOrDefault();
        if (file is null)
        {
            return;
        }

        FileName = file.Name;

        var properties = await file.GetBasicPropertiesAsync();
        _originalFileSizeBytes = (long)(properties.Size ?? 0);
        FileSize = FormatBytes(_originalFileSizeBytes);

        RecalculateEstimate();
    }

    [RelayCommand]
    private void SetResolution1080()
    {
        SelectedResolution = "1080p";
        RecalculateEstimate();
    }

    [RelayCommand]
    private void SetResolution720()
    {
        SelectedResolution = "720p";
        RecalculateEstimate();
    }

    [RelayCommand]
    private void SetResolution480()
    {
        SelectedResolution = "480p";
        RecalculateEstimate();
    }

    [RelayCommand]
    private async Task StartCompressionAsync()
    {
        if (!HasFile || IsProcessing)
        {
            return;
        }

        IsProcessing = true;
        ProgressValue = 0;
        ProgressPercent = "0%";

        // TODO: remplacer cette simulation par un vrai encodage vidéo
        // (ex: FFmpeg via un binding natif) une fois le moteur de compression choisi.
        const int steps = 20;
        for (var i = 1; i <= steps; i++)
        {
            await Task.Delay(150);
            ProgressValue = (double)i / steps;
            ProgressPercent = $"{ProgressValue:P0}";
            var remainingSeconds = (steps - i) * 0.15;
            CountdownText = TimeSpan.FromSeconds(remainingSeconds).ToString(@"mm\:ss");
        }

        IsProcessing = false;
    }

    private void RecalculateEstimate()
    {
        if (!HasFile)
        {
            EstimatedSize = "~0 MB";
            EstimatedReduction = "-0 MB";
            return;
        }

        // Heuristique simple: le CRF et la résolution cible influencent le ratio de compression.
        var resolutionFactor = SelectedResolution switch
        {
            "720p" => 0.55,
            "480p" => 0.3,
            _ => 0.85
        };

        var crfFactor = Math.Clamp(1.3 - (CompressionLevel - 18) * 0.04, 0.35, 1.0);

        var estimatedBytes = (long)(_originalFileSizeBytes * resolutionFactor * crfFactor);
        var reductionBytes = _originalFileSizeBytes - estimatedBytes;

        EstimatedSize = $"~{FormatBytes(estimatedBytes)}";
        EstimatedReduction = $"-{FormatBytes(reductionBytes)}";
    }

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return mb >= 1024
            ? $"{mb / 1024:0.##} GB"
            : $"{mb:0.#} MB";
    }
}
