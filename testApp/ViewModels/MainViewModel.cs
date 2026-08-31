using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using testApp.Services;

namespace testApp.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // Assigned by the View once it has access to the TopLevel (needed for the file picker).
    public IStorageProvider? StorageProvider { get; set; }

    private long _originalFileSizeBytes;
    private string? _localInputPath;

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

    [ObservableProperty]
    private string? _statusMessage;

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
        StatusMessage = null;

        // On copie systématiquement vers un chemin local temporaire : sur Android, le fichier
        // choisi vient souvent d'un content:// (Storage Access Framework) que ffmpeg ne sait
        // pas lire directement, donc on a besoin d'un vrai chemin sur disque.
        var extension = Path.GetExtension(file.Name);
        _localInputPath = Path.Combine(Path.GetTempPath(), $"input_{Guid.NewGuid():N}{extension}");

        await using (var sourceStream = await file.OpenReadAsync())
        await using (var destStream = File.Create(_localInputPath))
        {
            await sourceStream.CopyToAsync(destStream);
        }

        _originalFileSizeBytes = new FileInfo(_localInputPath).Length;
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
        if (!HasFile || IsProcessing || _localInputPath is null)
        {
            return;
        }

        var compressor = VideoCompressionServiceLocator.Current;
        if (compressor is null)
        {
            StatusMessage = "La compression vidéo n'est pas encore disponible sur cette plateforme.";
            return;
        }

        IsProcessing = true;
        StatusMessage = null;
        ProgressValue = 0;
        ProgressPercent = "0%";
        CountdownText = "--:--";

        var outputPath = Path.Combine(Path.GetTempPath(), $"output_{Guid.NewGuid():N}.mp4");
        var scaleFilter = SelectedResolution switch
        {
            "720p" => "1280:720",
            "480p" => "854:480",
            _ => null
        };

        var startedAt = DateTime.UtcNow;
        var progressReporter = new Progress<double>(fraction =>
        {
            ProgressValue = fraction;
            ProgressPercent = $"{fraction:P0}";

            var elapsed = DateTime.UtcNow - startedAt;
            if (fraction > 0.02)
            {
                var estimatedTotal = elapsed / fraction;
                var remaining = estimatedTotal - elapsed;
                if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                CountdownText = remaining.ToString(@"mm\:ss");
            }
        });

        try
        {
            await compressor.CompressAsync(_localInputPath, outputPath, CompressionLevel, scaleFilter, progressReporter, default);

            ProgressValue = 1;
            ProgressPercent = "100%";
            CountdownText = "00:00";

            var compressedSize = new FileInfo(outputPath).Length;
            EstimatedSize = FormatBytes(compressedSize);
            EstimatedReduction = $"-{FormatBytes(Math.Max(0, _originalFileSizeBytes - compressedSize))}";

            await SaveCompressedFileAsync(outputPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Échec de la compression : {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task SaveCompressedFileAsync(string compressedFilePath)
    {
        if (StorageProvider is null)
        {
            return;
        }

        var suggestedName = $"compressed_{Path.GetFileNameWithoutExtension(FileName)}.mp4";
        var destination = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Enregistrer la vidéo compressée",
            SuggestedFileName = suggestedName,
            DefaultExtension = "mp4"
        });

        if (destination is null)
        {
            return;
        }

        await using var sourceStream = File.OpenRead(compressedFilePath);
        await using var destStream = await destination.OpenWriteAsync();
        await sourceStream.CopyToAsync(destStream);
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
        // Ce n'est qu'une estimation affichée AVANT compression ; la vraie taille finale (après
        // encodage réel) remplace ces valeurs une fois StartCompressionCommand terminé.
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
