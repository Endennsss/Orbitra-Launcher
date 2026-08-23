using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System.ComponentModel;
using System.Linq;
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
        {
            _boundViewModel.FilteredConsoleEntries.CollectionChanged -= ConsoleLinesChanged;
            _boundViewModel.PropertyChanged -= ViewModelPropertyChanged;
        }
        _boundViewModel = DataContext as LocalServersTabViewModel;
        if (_boundViewModel != null)
        {
            _boundViewModel.FilteredConsoleEntries.CollectionChanged += ConsoleLinesChanged;
            _boundViewModel.PropertyChanged += ViewModelPropertyChanged;
        }
    }

    private void ConsoleLinesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_boundViewModel is { IsConsoleAutoScrollPaused: false } vm && vm.FilteredConsoleEntries.Count > 0)
            _consoleList?.ScrollIntoView(vm.FilteredConsoleEntries[^1]);
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LocalServersTabViewModel.SelectedConsoleEntry) && _boundViewModel?.SelectedConsoleEntry != null)
            _consoleList?.ScrollIntoView(_boundViewModel.SelectedConsoleEntry);
    }

    private void ConsoleInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not LocalServersTabViewModel vm) return;
        if (e.Key == Key.Up) { vm.NavigateCommandHistory(-1); e.Handled = true; return; }
        if (e.Key == Key.Down) { vm.NavigateCommandHistory(1); e.Handled = true; return; }
        if (e.Key == Key.Enter && vm.SendCommand.CanExecute(null)) { vm.SendCommand.Execute(null); e.Handled = true; }
    }

    private async void CopySelectedConsoleLinesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entries = _consoleList?.SelectedItems?.OfType<LocalConsoleLineViewModel>().ToArray();
        if (entries is not { Length: > 0 } || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(string.Join(System.Environment.NewLine, entries.Select(x => x.Text)));
    }
}
