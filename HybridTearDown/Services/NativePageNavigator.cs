using Microsoft.Extensions.Logging;

namespace HybridTearDown.Services;

public sealed class NativePageNavigator(IServiceProvider services)
{
    public void ShowNativePage()
        => ReplaceWindowPage(services.GetRequiredService<NativePage>(), "native");

    public void ShowBlazorPage()
        => ReplaceWindowPage(services.GetRequiredService<MainPage>(), "Blazor");

    private static void ReplaceWindowPage(Page page, string destination)
    {
        // BeginInvokeOnMainThread only QUEUES this work. Anything that must happen
        // after the swap has to live inside the callback, not after the call.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var window = Application.Current?.Windows.FirstOrDefault()
                ?? throw new InvalidOperationException("No MAUI window is available.");

            TeardownDiagnostics.MarkNativeNavigation($"{destination} page");

            EvidenceLog.Write(
                "NativeNavigation",
                LogLevel.Information,
                $"[NativeNavigation] Replacing window content with {destination} page.");

            window.Page = page;

            // The swap has now been applied. When a BlazorWebView is replaced or
            // removed from the visual tree, MAUI's framework automatically triggers
            // the HandlerChanged event on the BlazorWebView to signal handler disconnection.
            // MainPage.xaml.cs subscribes to BlazorHost.HandlerChanged and calls
            // TeardownDiagnostics.MarkWebViewDestroyed() or MarkWebViewCreated()
            // based on whether the handler is null or non-null.
        });
    }
}
