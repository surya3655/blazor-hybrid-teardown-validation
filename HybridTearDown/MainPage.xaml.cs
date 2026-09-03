using HybridTearDown.Services;

namespace HybridTearDown;

public partial class MainPage : ContentPage
{
    private readonly NativePageNavigator _navigator;

    public MainPage(NativePageNavigator navigator)
    {
        InitializeComponent();
        TeardownDiagnostics.TrackPage(this);
        TeardownDiagnostics.TrackWebView(BlazorHost);
        TeardownDiagnostics.MarkWebViewCreated();
        _navigator = navigator;
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

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
            TeardownDiagnostics.MarkWebViewDestroyed();
        else
            TeardownDiagnostics.MarkWebViewCreated();
    }

    private void OnNativeScreenClicked(object? sender, EventArgs e)
    {
        _navigator.ShowNativePage();
    }
}