using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using testApp.Android.Services;
using testApp.Services;

namespace testApp.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            VideoCompressionServiceLocator.Current = new AndroidFFmpegVideoCompressionService();

            return base.CustomizeAppBuilder(builder)
            .WithInterFont();
        }
    }
}
