using Microsoft.Extensions.Logging;

namespace HybridTearDown.Services;

public sealed class NativePageNavigator(IServiceProvider services)
{
    public void ShowNativePage()
    {
        TeardownDiagnostics.MarkNativeNavigation("native page");
        ReplaceWindowPage(services.GetRequiredService<NativePage>(), "native");
        TeardownDiagnostics.MarkWebViewDestroyed();      // then record it

    }

    public void ShowBlazorPage()
    {
        TeardownDiagnostics.MarkNativeNavigation("Blazor page");
        ReplaceWindowPage(services.GetRequiredService<MainPage>(), "Blazor");
        TeardownDiagnostics.MarkWebViewCreated();

    }

    private static void ReplaceWindowPage(Page page, string destination)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Application.Current?.Windows.FirstOrDefault()
                ?? throw new InvalidOperationException("No MAUI window is available.");

            EvidenceLog.Write("NativeNavigation", LogLevel.Information, $"[NativeNavigation] Replacing window content with {destination} page.");
            window.Page = page;
        });
    }
}