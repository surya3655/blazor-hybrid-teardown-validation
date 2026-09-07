using HybridTearDown.Services;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace HybridTearDown;

public partial class MainPage : ContentPage
{
    private readonly NativePageNavigator _navigator;

    public MainPage(NativePageNavigator navigator)
    {
        InitializeComponent();

        TeardownDiagnostics.TrackPage(this);
        TeardownDiagnostics.TrackWebView(BlazorHost);

        _navigator = navigator;

        // Observe the BlazorWebView's handler lifecycle to track webview destruction.
        // This must be done after InitializeComponent so BlazorHost (x:Name="BlazorHost")
        // from XAML is available.
        BlazorHost.HandlerChanged += OnBlazorWebViewHandlerChanged;
    }

    /// <summary>
    /// Responds to BlazorWebView handler lifecycle changes.
    /// When Handler becomes null, the webview is being destroyed.
    /// When Handler becomes non-null, a new webview instance is being created.
    /// </summary>
    private void OnBlazorWebViewHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is BlazorWebView webView)
        {
            if (webView.Handler == null)
            {
                // Handler is being removed — webview destruction is underway.
                TeardownDiagnostics.MarkWebViewDestroyed();
            }
            else
            {
                // Handler has been attached — a fresh webview has been created.
                TeardownDiagnostics.MarkWebViewCreated();
            }
        }
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
            TeardownDiagnostics.MarkPageDetached();
        else
            TeardownDiagnostics.MarkPageAttached();
    }

    private void OnCloseAppClicked(object? sender, EventArgs e)
    {
        //TeardownDiagnostics.BeginShutdown();   // logged while still alive

    #if ANDROID
        Platform.CurrentActivity?.FinishAndRemoveTask();
    #else
        Application.Current?.Quit();
    #endif
    }
    private void OnNativeScreenClicked(object? sender, EventArgs e)
    {
        _navigator.ShowNativePage();
    }
}