using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using SS14.Launcher.ViewModels.MainWindowTabs;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class ServerListTabView : UserControl
{
    private ServerListTabViewModel? _viewModel;
    public ServerListTabView()
    {
        InitializeComponent();
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
}
