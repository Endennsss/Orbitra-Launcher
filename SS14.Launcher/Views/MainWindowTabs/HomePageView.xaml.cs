using System;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Serilog;
using SS14.Launcher.ViewModels;
using SS14.Launcher.ViewModels.MainWindowTabs;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class HomePageView : UserControl
{
    private HomePageViewModel? _viewModel;

    public HomePageView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateAdaptiveLayout();

        // The saved layout is applied without showing another page first.
        // Only deliberate layout changes made after startup use a transition.
        Loaded += (_, _) => Dispatcher.UIThread.Post(
            () => FavoriteLayoutCarousel.PageTransition ??= new CrossFade(TimeSpan.FromMilliseconds(280)),
            DispatcherPriority.Background);
    }

    private void UpdateAdaptiveLayout()
    {
        if (Bounds.Width <= 0)
            return;

        if (Bounds.Width < 900)
        {
            var listHeight = Math.Clamp(Bounds.Height * 0.36, 190, 290);
            FavoriteSplitGrid.ColumnDefinitions = new ColumnDefinitions("*");
            FavoriteSplitGrid.RowDefinitions = new RowDefinitions($"{listHeight:0},5,*");
            Grid.SetColumn(FavoriteSplitListPane, 0);
            Grid.SetRow(FavoriteSplitListPane, 0);
            Grid.SetColumn(FavoriteSplitDivider, 0);
            Grid.SetRow(FavoriteSplitDivider, 1);
            Grid.SetColumn(FavoriteSplitDetailPane, 0);
            Grid.SetRow(FavoriteSplitDetailPane, 2);
            FavoriteSplitDivider.ResizeDirection = GridResizeDirection.Rows;
            FavoriteSplitListPane.MinWidth = 0;
            FavoriteSplitDetailPane.MinWidth = 0;
        }
        else
        {
            var listWidth = Math.Clamp(Bounds.Width * 0.34, 320, 430);
            FavoriteSplitGrid.ColumnDefinitions = new ColumnDefinitions($"{listWidth:0},5,*");
            FavoriteSplitGrid.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(FavoriteSplitListPane, 0);
            Grid.SetRow(FavoriteSplitListPane, 0);
            Grid.SetColumn(FavoriteSplitDivider, 1);
            Grid.SetRow(FavoriteSplitDivider, 0);
            Grid.SetColumn(FavoriteSplitDetailPane, 2);
            Grid.SetRow(FavoriteSplitDetailPane, 0);
            FavoriteSplitDivider.ResizeDirection = GridResizeDirection.Columns;
            FavoriteSplitListPane.MinWidth = 280;
            FavoriteSplitDetailPane.MinWidth = 460;
        }
    }

    private void FavoriteFocusSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null || sender is not ListBox { SelectedItem: ServerEntryViewModel server })
            return;

        _viewModel.SelectedServer = server;
        _viewModel.OpenSelectedServer();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.Control = null;
        }

        _viewModel = DataContext as HomePageViewModel;

        if (_viewModel != null)
        {
            _viewModel.Control = this;
        }

        base.OnDataContextChanged(e);
    }

    private async void OpenReplayClicked(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.MainWindowViewModel is not { } mainVm)
            return;

        if (this.GetVisualRoot() is not Window window)
        {
            Log.Error("Visual root isn't a window!");
            return;
        }

        var result = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select replay or content bundle file",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Replay or content bundle files")
                {
                    Patterns = ["*.zip"],
                    MimeTypes = ["application/zip"],
                    AppleUniformTypeIdentifiers = ["zip"]
                }
            ]
        });

        if (result.Count == 0) // Cancelled
            return;

        using var file = result[0];
        if (!mainVm.IsContentBundleDropValid(file))
        {
            // TODO: Report this nicely.
            return;
        }

        ConnectingViewModel.StartContentBundle(mainVm, file);
    }
}
