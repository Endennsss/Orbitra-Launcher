using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using SS14.Launcher.ViewModels.MainWindowTabs;
using Avalonia.Input;
using Avalonia.Animation;
using Avalonia.Threading;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class ServerListTabView : UserControl
{
    private ServerListTabViewModel? _viewModel;
    public ServerListTabView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateAdaptiveLayout();

        // Do not animate the initial binding from page zero to the saved layout.
        // Once the first layout pass is complete, later user changes cross-fade smoothly.
        Loaded += (_, _) => Dispatcher.UIThread.Post(
            () => ServerLayoutCarousel.PageTransition ??= new CrossFade(TimeSpan.FromMilliseconds(280)),
            DispatcherPriority.Background);
    }

    private void UpdateAdaptiveLayout()
    {
        var width = Bounds.Width;
        if (width <= 0)
            return;

        SetClass("Narrow", width < 760);
        SetClass("Compact", width < 620);

        // Variant 2 keeps both panes usable instead of allowing the splitter to
        // squeeze the detail header until its title and actions overlap.
        var stackPanes = width < 900;
        if (stackPanes)
        {
            var listHeight = Math.Clamp(Bounds.Height * 0.36, 190, 290);
            SplitLayoutGrid.ColumnDefinitions = new ColumnDefinitions("*");
            SplitLayoutGrid.RowDefinitions = new RowDefinitions($"{listHeight:0},5,*");

            Grid.SetColumn(SplitListPane, 0);
            Grid.SetRow(SplitListPane, 0);
            Grid.SetColumn(SplitDivider, 0);
            Grid.SetRow(SplitDivider, 1);
            Grid.SetColumn(SplitDetailPane, 0);
            Grid.SetRow(SplitDetailPane, 2);
            SplitDivider.ResizeDirection = GridResizeDirection.Rows;
            SplitListPane.MinWidth = 0;
            SplitDetailPane.MinWidth = 0;
        }
        else
        {
            var listWidth = Math.Clamp(width * 0.34, 320, 430);
            SplitLayoutGrid.ColumnDefinitions = new ColumnDefinitions($"{listWidth:0},5,*");
            SplitLayoutGrid.RowDefinitions = new RowDefinitions("*");

            Grid.SetColumn(SplitListPane, 0);
            Grid.SetRow(SplitListPane, 0);
            Grid.SetColumn(SplitDivider, 1);
            Grid.SetRow(SplitDivider, 0);
            Grid.SetColumn(SplitDetailPane, 2);
            Grid.SetRow(SplitDetailPane, 0);
            SplitDivider.ResizeDirection = GridResizeDirection.Columns;
            SplitListPane.MinWidth = 280;
            SplitDetailPane.MinWidth = 460;
        }
    }

    private void SetClass(string name, bool enabled)
    {
        if (enabled)
        {
            if (!Classes.Contains(name))
                Classes.Add(name);
        }
        else
        {
            Classes.Remove(name);
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel != null)
            _viewModel.SearchFocusRequested -= FocusSearch;
        _viewModel = DataContext as ServerListTabViewModel;
        if (_viewModel != null)
            _viewModel.SearchFocusRequested += FocusSearch;
        base.OnDataContextChanged(e);
    }

    private void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void ServerListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel?.SelectedServer is { CanConnect: true } server)
            server.ConnectPressed();
    }

    private void FocusServerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null || sender is not ListBox { SelectedItem: ServerEntryViewModel server })
            return;
        _viewModel.SelectedServer = server;
        _viewModel.OpenSelectedServer();
    }
}
