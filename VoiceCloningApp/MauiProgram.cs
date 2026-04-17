using Microsoft.Extensions.Logging;
using VoiceCloningApp.Services;

namespace VoiceCloningApp;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Blazor Hybrid
        builder.Services.AddMauiBlazorWebView();

        // HTTP client pointed at the local Python backend
        builder.Services.AddHttpClient<TtsApiService>(client =>
        {
            client.BaseAddress = new Uri("http://localhost:8000");
            client.Timeout = TimeSpan.FromMinutes(10); // TTS generation can be slow
        });

        // Local embedding storage
        builder.Services.AddSingleton<EmbeddingStorageService>();

        // Backend model detection and process management
        builder.Services.AddSingleton<ModelSetupService>();
        builder.Services.AddSingleton<BackendProcessService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
