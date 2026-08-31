using System;
using System.Threading;
using System.Threading.Tasks;

namespace testApp.Services;

/// <summary>
/// Cross-platform contract for actually encoding/compressing a video file.
/// Each platform head (Android, Desktop, ...) provides its own implementation
/// and registers it into <see cref="VideoCompressionServiceLocator"/> at startup.
/// </summary>
public interface IVideoCompressionService
{
    /// <param name="inputFilePath">Local filesystem path to the source video.</param>
    /// <param name="outputFilePath">Local filesystem path where the compressed video should be written.</param>
    /// <param name="crf">Constant Rate Factor (18 = high quality/bigger file, 35 = low quality/smaller file).</param>
    /// <param name="scaleFilter">Optional "width:height" ffmpeg scale filter, e.g. "1280:720". Null/empty keeps original resolution.</param>
    /// <param name="progress">Reports compression progress from 0.0 to 1.0.</param>
    Task CompressAsync(
        string inputFilePath,
        string outputFilePath,
        int crf,
        string? scaleFilter,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Simple service locator so the shared MainViewModel can reach the platform-specific
/// compression engine without the shared project needing to reference Android/iOS APIs.
/// Set once at app startup by each platform head.
/// </summary>
public static class VideoCompressionServiceLocator
{
    public static IVideoCompressionService? Current { get; set; }
}
