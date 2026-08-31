using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using testApp.Services;

// NOTE: the exact namespace/class names below come from the FFmpegKit Android
// binding package (a community-maintained .NET binding, not officially supported
// by Microsoft). If the build fails on "namespace/class not found" here, open the
// package's DLL in the CI log or its NuGet page to see the real namespace and we
// adjust these usings/calls accordingly — the logic itself won't need to change.
using FFMpegKit.Droid;

namespace testApp.Android.Services;

public class AndroidFFmpegVideoCompressionService : IVideoCompressionService
{
    public Task CompressAsync(
        string inputFilePath,
        string outputFilePath,
        int crf,
        string? scaleFilter,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var durationSeconds = TryGetDurationSeconds(inputFilePath);

        var scaleArg = string.IsNullOrWhiteSpace(scaleFilter)
            ? string.Empty
            : $"-vf scale={scaleFilter} ";

        var command =
            $"-y -i \"{inputFilePath}\" {scaleArg}" +
            $"-c:v libx264 -crf {crf} -preset medium -c:a aac -b:a 128k \"{outputFilePath}\"";

        var completeCallback = new SessionCompleteCallback(session =>
        {
            if (ReturnCode.IsSuccess(session.ReturnCode))
            {
                progress?.Report(1.0);
                tcs.TrySetResult();
            }
            else if (ReturnCode.IsCancel(session.ReturnCode))
            {
                tcs.TrySetCanceled();
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException(
                    $"FFmpeg a échoué (code {session.ReturnCode}). Logs: {session.AllLogsAsString}"));
            }
        });

        var logCallback = new NoOpLogCallback();

        var statisticsCallback = new StatisticsCallbackImpl(statistics =>
        {
            if (durationSeconds > 0)
            {
                var elapsedSeconds = statistics.Time / 1000.0;
                var fraction = elapsedSeconds / durationSeconds;
                progress?.Report(Math.Clamp(fraction, 0, 0.99));
            }
        });

        global::FFMpegKit.Droid.FFmpegKit.ExecuteAsync(command, completeCallback, logCallback, statisticsCallback);

        cancellationToken.Register(() => global::FFMpegKit.Droid.FFmpegKit.Cancel());

        return tcs.Task;
    }

    private static double TryGetDurationSeconds(string inputFilePath)
    {
        try
        {
            var info = FFprobeKit.GetMediaInformation(inputFilePath);
            var durationText = info?.MediaInformation?.Duration;
            return double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                ? seconds
                : 0;
        }
        catch
        {
            // Si ffprobe échoue à estimer la durée, on continue sans pourcentage précis :
            // la compression tournera quand même, seule la barre de progression sera approximative.
            return 0;
        }
    }

    // Ces trois classes existent uniquement parce que les callbacks FFmpegKit sont des
    // interfaces Java (pas des delegates C#) : on doit fournir une vraie implémentation
    // dérivant de Java.Lang.Object pour que le pont JNI fonctionne.
    private sealed class SessionCompleteCallback : Java.Lang.Object, IFFmpegSessionCompleteCallback
    {
        private readonly Action<FFmpegSession> _onComplete;
        public SessionCompleteCallback(Action<FFmpegSession> onComplete) => _onComplete = onComplete;
        public void Apply(FFmpegSession session) => _onComplete(session);
    }

    private sealed class NoOpLogCallback : Java.Lang.Object, ILogCallback
    {
        public void Apply(Log log)
        {
            // Logs bruts ffmpeg, ignorés pour l'instant — utile pour déboguer si besoin plus tard.
        }
    }

    private sealed class StatisticsCallbackImpl : Java.Lang.Object, IStatisticsCallback
    {
        private readonly Action<Statistics> _onStatistics;
        public StatisticsCallbackImpl(Action<Statistics> onStatistics) => _onStatistics = onStatistics;
        public void Apply(Statistics statistics) => _onStatistics(statistics);
    }
}
