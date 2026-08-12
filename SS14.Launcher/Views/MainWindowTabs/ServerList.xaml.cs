using System;
using Avalonia;
using Avalonia.Controls.Primitives;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Metadata;
using Microsoft.Toolkit.Mvvm.Input;
using Serilog;
using SS14.Launcher.ViewModels.MainWindowTabs;

namespace SS14.Launcher.Views.MainWindowTabs;

public sealed partial class ServerList : TemplatedControl
{
    private enum SortColumn { None, Name, RoundTime, Players, Ping }
    private SortColumn _sortColumn;
    private bool _sortDescending;
    private INotifyCollectionChanged? _observableList;

    public static readonly DirectProperty<ServerList, ObservableCollection<ServerEntryViewModel>> DisplayListProperty =
        AvaloniaProperty.RegisterDirect<ServerList, ObservableCollection<ServerEntryViewModel>>(nameof(DisplayList), o => o.DisplayList);
    public static readonly DirectProperty<ServerList, ICommand> SortNameCommandProperty =
        AvaloniaProperty.RegisterDirect<ServerList, ICommand>(nameof(SortNameCommand), o => o.SortNameCommand);
    public static readonly DirectProperty<ServerList, ICommand> SortPlayersCommandProperty =
        AvaloniaProperty.RegisterDirect<ServerList, ICommand>(nameof(SortPlayersCommand), o => o.SortPlayersCommand);
    public static readonly DirectProperty<ServerList, ICommand> SortPingCommandProperty =
        AvaloniaProperty.RegisterDirect<ServerList, ICommand>(nameof(SortPingCommand), o => o.SortPingCommand);
    public static readonly DirectProperty<ServerList, ICommand> SortRoundTimeCommandProperty =
        AvaloniaProperty.RegisterDirect<ServerList, ICommand>(nameof(SortRoundTimeCommand), o => o.SortRoundTimeCommand);

    public ObservableCollection<ServerEntryViewModel> DisplayList { get; } = [];
    public ICommand SortNameCommand { get; }
    public ICommand SortPlayersCommand { get; }
    public ICommand SortPingCommand { get; }
    public ICommand SortRoundTimeCommand { get; }

    public ServerList()
    {
        SortNameCommand = new RelayCommand(() => ApplySort(SortColumn.Name));
        SortPlayersCommand = new RelayCommand(() => ApplySort(SortColumn.Players));
        SortPingCommand = new RelayCommand(() => ApplySort(SortColumn.Ping));
        SortRoundTimeCommand = new RelayCommand(() => ApplySort(SortColumn.RoundTime));
    }

    public static readonly DirectProperty<ServerList, bool> ShowHeaderProperty =
        AvaloniaProperty.RegisterDirect<ServerList, bool>(
            nameof(ShowHeader),
            o => o.ShowHeader,
            (o, v) => o.ShowHeader = v
        );

    private bool _showHeader;

    public bool ShowHeader
    {
        get => _showHeader;
        set => SetAndRaise(ShowHeaderProperty, ref _showHeader, value);
    }

    public static readonly DirectProperty<ServerList, string?> ListTextProperty =
        AvaloniaProperty.RegisterDirect<ServerList, string?>(
            nameof(ListText),
            o => o.ListText,
            (o, v) => o.ListText = v
        );

    private string? _listText;

    /// <summary>
    /// Optional text which will be displayed in the server list area.
    /// If null or empty no text will be added.
    /// </summary>
    public string? ListText
    {
        get => _listText;
        set => SetAndRaise(ListTextProperty, ref _listText, value);
    }

    public static readonly DirectProperty<ServerList, bool> SpinnerVisibleProperty =
        AvaloniaProperty.RegisterDirect<ServerList, bool>(
            nameof(SpinnerVisible),
            o => o.SpinnerVisible,
            (o, v) => o.SpinnerVisible = v
        );

    private bool _spinnerVisible;

    public bool SpinnerVisible
    {
        get => _spinnerVisible;
        set => SetAndRaise(SpinnerVisibleProperty, ref _spinnerVisible, value);
    }

    public static readonly DirectProperty<ServerList, IReadOnlyCollection<ServerEntryViewModel>> ListProperty =
        AvaloniaProperty.RegisterDirect<ServerList, IReadOnlyCollection<ServerEntryViewModel>>(
            nameof(List),
            o => o.List,
            (o, v) => o.List = v
        );

    private IReadOnlyCollection<ServerEntryViewModel> _serverList = Array.Empty<ServerEntryViewModel>();

    public IReadOnlyCollection<ServerEntryViewModel> List
    {
        get => _serverList;
        set
        {
            if (_observableList != null)
                _observableList.CollectionChanged -= SourceCollectionChanged;
            SetAndRaise(ListProperty, ref _serverList, value);
            _observableList = value as INotifyCollectionChanged;
            if (_observableList != null)
                _observableList.CollectionChanged += SourceCollectionChanged;
            RefreshDisplayList();
        }
    }

    public string NameSortMark => SortMark(SortColumn.Name);
    public string PlayersSortMark => SortMark(SortColumn.Players);
    public string PingSortMark => SortMark(SortColumn.Ping);
    public string RoundTimeSortMark => SortMark(SortColumn.RoundTime);

    private void SourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshDisplayList();

    private void ApplySort(SortColumn column)
    {
        var oldNameMark = NameSortMark;
        var oldPlayersMark = PlayersSortMark;
        var oldPingMark = PingSortMark;
        var oldRoundTimeMark = RoundTimeSortMark;
        if (_sortColumn == column)
            _sortDescending = !_sortDescending;
        else
        {
            _sortColumn = column;
            _sortDescending = column == SortColumn.Players;
        }
        RaisePropertyChanged(NameSortMarkProperty, oldNameMark, NameSortMark);
        RaisePropertyChanged(PlayersSortMarkProperty, oldPlayersMark, PlayersSortMark);
        RaisePropertyChanged(PingSortMarkProperty, oldPingMark, PingSortMark);
        RaisePropertyChanged(RoundTimeSortMarkProperty, oldRoundTimeMark, RoundTimeSortMark);
        RefreshDisplayList();
    }

    private void RefreshDisplayList()
    {
        IEnumerable<ServerEntryViewModel> items = _serverList;
        items = _sortColumn switch
        {
            SortColumn.Name => _sortDescending
                ? items.OrderByDescending(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                : items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
            SortColumn.Players => _sortDescending
                ? items.OrderByDescending(x => x.CacheData.PlayerCount)
                : items.OrderBy(x => x.CacheData.PlayerCount),
            SortColumn.Ping => _sortDescending
                ? items.OrderByDescending(x => x.CacheData.Ping ?? TimeSpan.MinValue)
                : items.OrderBy(x => x.CacheData.Ping ?? TimeSpan.MaxValue),
            SortColumn.RoundTime => _sortDescending
                ? items.OrderByDescending(x => RoundDuration(x) ?? TimeSpan.MinValue)
                : items.OrderBy(x => RoundDuration(x) ?? TimeSpan.MaxValue),
            _ => items
        };
        DisplayList.Clear();
        foreach (var item in items)
            DisplayList.Add(item);
    }

    private static TimeSpan? RoundDuration(ServerEntryViewModel item) =>
        item.RoundStartTime is { } start ? DateTime.UtcNow - start.ToUniversalTime() : null;

    private string SortMark(SortColumn column) => _sortColumn == column ? (_sortDescending ? "↓" : "↑") : string.Empty;

    public static readonly DirectProperty<ServerList, string> NameSortMarkProperty =
        AvaloniaProperty.RegisterDirect<ServerList, string>(nameof(NameSortMark), o => o.NameSortMark);
    public static readonly DirectProperty<ServerList, string> PlayersSortMarkProperty =
        AvaloniaProperty.RegisterDirect<ServerList, string>(nameof(PlayersSortMark), o => o.PlayersSortMark);
    public static readonly DirectProperty<ServerList, string> PingSortMarkProperty =
        AvaloniaProperty.RegisterDirect<ServerList, string>(nameof(PingSortMark), o => o.PingSortMark);
    public static readonly DirectProperty<ServerList, string> RoundTimeSortMarkProperty =
        AvaloniaProperty.RegisterDirect<ServerList, string>(nameof(RoundTimeSortMark), o => o.RoundTimeSortMark);

    public static readonly StyledProperty<object?> ContentProperty =
        ContentControl.ContentProperty.AddOwner<ServerList>();

    /// <summary>
    /// If an optional content block is provided it will be
    /// shown at the bottom of the server list.
    /// </summary>
    [Content]
    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}
