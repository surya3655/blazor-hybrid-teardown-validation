using HybridTearDown.Services;
using Microsoft.Extensions.Logging;

namespace HybridTearDown;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        EvidenceLog.StartSession();
        // if Write(string category, string message)
TeardownDiagnostics.Sink = msg =>
    EvidenceLog.Write("Diagnostics", LogLevel.Information, msg, null);
// if Write(LogLevel level, string category, string message)
//TeardownDiagnostics.Sink = msg => EvidenceLog.Write(LogLevel.Information, "Diagnostics", msg);

// if Write(string message, string category)
//TeardownDiagnostics.Sink = msg => EvidenceLog.Write(msg, "Diagnostics");
        // use your existing writer
        TeardownDiagnostics.Install();
        TeardownDiagnostics.StartMemorySampling(TimeSpan.FromSeconds(5));

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>();

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<NativePageNavigator>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<NativePage>();
        builder.Logging.AddProvider(new EvidenceLoggerProvider());

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}