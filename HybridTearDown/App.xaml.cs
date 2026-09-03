using HybridTearDown.Services;
using Microsoft.Extensions.Logging;

namespace HybridTearDown;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;

        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            EvidenceLog.Write("UnhandledException", LogLevel.Critical, "Unhandled application exception.", eventArgs.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            EvidenceLog.Write("UnobservedTaskException", LogLevel.Error, "Unobserved task exception.", eventArgs.Exception);
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_services.GetRequiredService<MainPage>());
        window.Created += (_, _) => EvidenceLog.Write("Lifecycle", LogLevel.Information, "Window created.");
        window.Activated += (_, _) => EvidenceLog.Write("Lifecycle", LogLevel.Information, "Window activated.");
        window.Deactivated += (_, _) => EvidenceLog.Write("Lifecycle", LogLevel.Information, "Window deactivated.");
        window.Stopped += (_, _) => EvidenceLog.Write("Lifecycle", LogLevel.Information, "Window stopped/backgrounded.");
        window.Resumed += (_, _) => EvidenceLog.Write("Lifecycle", LogLevel.Information, "Window resumed.");
        window.Destroying += (_, _) =>
        {
            TeardownDiagnostics.BeginShutdown();
            TeardownDiagnostics.EndShutdown();
        };
        //window.Destroying += (_, _) => EvidenceLog.Write("Lifecycle", LogLevel.Information, "Window destroying.");
        return window;
    }

    // protected override void OnDestroying()   // or the Destroying event
    // {
    //     TeardownDiagnostics.BeginShutdown();
    //     base.OnDestroying();
    //     TeardownDiagnostics.EndShutdown();
    // }

}