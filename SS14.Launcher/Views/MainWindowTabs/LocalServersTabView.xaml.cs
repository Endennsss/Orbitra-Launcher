using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SS14.Launcher.ViewModels.MainWindowTabs;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class LocalServersTabView : UserControl
{
    private ListBox? _consoleList;

    public LocalServersTabView()
    {
        AvaloniaXamlLoader.Load(this);
        _consoleList = this.FindControl<ListBox>("ConsoleList");
        DataContextChanged += (_, _) => BindConsoleScroll();
        BindConsoleScroll();
    }

    private LocalServersTabViewModel? _boundViewModel;

    private void BindConsoleScroll()
    {
        if (_boundViewModel != null)
            _boundViewModel.ConsoleLines.CollectionChanged -= ConsoleLinesChanged;
        _boundViewModel = DataContext as LocalServersTabViewModel;
        if (_boundViewModel != null)
            _boundViewModel.ConsoleLines.CollectionChanged += ConsoleLinesChanged;
    }

    private void ConsoleLinesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_boundViewModel?.ConsoleLines.Count > 0)
            _consoleList?.ScrollIntoView(_boundViewModel.ConsoleLines[^1]);
    }

    private void ConsoleInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not LocalServersTabViewModel vm || !vm.SendCommand.CanExecute(null)) return;
        vm.SendCommand.Execute(null);
        e.Handled = true;
    }
}
