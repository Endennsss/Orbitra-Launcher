using Avalonia.Controls;

namespace SS14.Launcher.Views;

public sealed partial class MainWindowContent : UserControl
{
    private bool _navigationSoundReady;
    private int _lastNavigationIndex = -1;

    public MainWindowContent()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _lastNavigationIndex = NavigationList.SelectedIndex;
            _navigationSoundReady = true;
        };
    }

    private void NavigationSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_navigationSoundReady || NavigationList.SelectedIndex == _lastNavigationIndex)
            return;

        _lastNavigationIndex = NavigationList.SelectedIndex;
        UiSoundService.PlayNavigation();

        // Re-arm the lightweight entrance animation for the newly selected
        // view. Starting almost opaque avoids flashing on data-heavy pages.
        PageHost.Classes.Remove("PagePulse");
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => PageHost.Classes.Add("PagePulse"),
            Avalonia.Threading.DispatcherPriority.Render);
    }
}
