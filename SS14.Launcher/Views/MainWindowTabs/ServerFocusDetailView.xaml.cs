using System;
using Avalonia;
using Avalonia.Controls;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class ServerFocusDetailView : UserControl
{
    public static readonly StyledProperty<bool> ShowNavigationProperty =
        AvaloniaProperty.Register<ServerFocusDetailView, bool>(nameof(ShowNavigation), true);

    public bool ShowNavigation
    {
        get => GetValue(ShowNavigationProperty);
        set => SetValue(ShowNavigationProperty, value);
    }

    public ServerFocusDetailView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateAdaptiveClasses();
    }

    private void UpdateAdaptiveClasses()
    {
        var narrow = Bounds.Width is > 0 and < 760;
        var compact = Bounds.Width is > 0 and < 620;
        SetClass("Narrow", narrow);
        SetClass("Compact", compact);

        ConfigurePair(FocusInfoGrid, FocusInfoSecond, compact);
        ConfigurePair(FocusGraphGrid, FocusGraphSecond, compact);

        if (compact)
        {
            FocusFooterGrid.ColumnDefinitions = new ColumnDefinitions("*");
            FocusFooterGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(FocusConnectButton, 0);
            Grid.SetRow(FocusConnectButton, 1);
            FocusConnectButton.Width = double.NaN;
            FocusConnectButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            FocusConnectButton.Margin = new Avalonia.Thickness(0, 8, 0, 0);
        }
        else
        {
            FocusFooterGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            FocusFooterGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(FocusConnectButton, 1);
            Grid.SetRow(FocusConnectButton, 0);
            FocusConnectButton.Width = 190;
            FocusConnectButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            FocusConnectButton.Margin = default;
        }
    }

    private static void ConfigurePair(Grid grid, Border second, bool compact)
    {
        if (compact)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*");
            grid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(second, 0);
            Grid.SetRow(second, 1);
            second.Margin = new Avalonia.Thickness(0, 10, 0, 0);
        }
        else
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*,*");
            grid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(second, 1);
            Grid.SetRow(second, 0);
            second.Margin = new Avalonia.Thickness(6, 0, 0, 0);
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
}
