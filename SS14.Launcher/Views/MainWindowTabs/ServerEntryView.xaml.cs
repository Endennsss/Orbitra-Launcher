using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.LogicalTree;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using SS14.Launcher.ViewModels.MainWindowTabs;

namespace SS14.Launcher.Views.MainWindowTabs;

public partial class ServerEntryView : UserControl
{
    public ServerEntryView()
    {
        InitializeComponent();

        Links.LayoutUpdated += ApplyStyle;
        SizeChanged += (_, _) => UpdateResponsiveHeader();
    }

    private void UpdateResponsiveHeader()
    {
        if (Bounds.Width <= 0)
            return;

        var hideRoundTime = Bounds.Width < 650;
        ServerRowGrid.ColumnDefinitions = Bounds.Width switch
        {
            < 520 => new ColumnDefinitions("*,0,0,82,192"),
            < 650 => new ColumnDefinitions("*,0,82,90,192"),
            _ => new ColumnDefinitions("*,88,96,100,236")
        };

        RowPingButton.IsVisible = !hideRoundTime;
    }

    // Sets the style for the link buttons correctly so that they look correct
    private void ApplyStyle(object? _1, EventArgs _2)
    {
        for (var i = 0; i < Links.ItemCount; i++)
        {
            if (Links.ContainerFromIndex(i) is not ContentPresenter { Child: ServerInfoLinkControl control } presenter)
                continue;

            presenter.ApplyTemplate();

            if (Links.ItemCount == 1)
                return;

            var style = i switch
            {
                0 => "OpenRight",
                _ when i == Links.ItemCount - 1 => "OpenLeft",
                _ => "OpenBoth",
            };

            control.GetLogicalChildren().OfType<Button>().FirstOrDefault()?.Classes.Add(style);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is ObservableRecipient r)
            r.IsActive = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (DataContext is ServerEntryViewModel { ViewedInFavoritesPane: true })
            return;

        if (DataContext is ObservableRecipient r)
            r.IsActive = false;
    }
}
