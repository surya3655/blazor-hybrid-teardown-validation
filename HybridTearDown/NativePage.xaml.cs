using HybridTearDown.Services;

namespace HybridTearDown;

public partial class NativePage : ContentPage
{
    private readonly NativePageNavigator _navigator;

    public NativePage(NativePageNavigator navigator)
    {
        InitializeComponent();
        _navigator = navigator;
        
        EvidencePathLabel.Text = $"Evidence log: {EvidenceLog.FilePath}";
    }
private void OnMemoryReportClicked(object? sender, EventArgs e)
    => TeardownDiagnostics.DrainAndReport("checkpoint");
    private void OnReturnClicked(object? sender, EventArgs e)
    {
        _navigator.ShowBlazorPage();
    }

    protected override bool OnBackButtonPressed()
    {
        _navigator.ShowBlazorPage();
        return true;
    }
}