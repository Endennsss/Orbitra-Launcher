using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Splat;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.ServerStatus;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public partial class ServerListTabViewModel : MainWindowTabViewModel
{
    private readonly LocalizationManager _loc = LocalizationManager.Instance;
    private readonly MainWindowViewModel _windowVm;
    private readonly ServerListCache _serverListCache;
    private readonly List<ServerStatusData> _badgeServers = [];
    private ServerEntryViewModel? _selectedServer;
    private ServerSortMode _sortMode = ServerSortMode.Players;
    private bool _sortDescending = true;
    private bool _isFocusedDetailOpen;

    public ObservableList<ServerEntryViewModel> SearchedServers { get; } = [];
    public event Action? SearchFocusRequested;

    private string? _searchString;
    private readonly DispatcherTimer _searchThrottle = new() { Interval = TimeSpan.FromMilliseconds(200) };

    public override string Name => _loc.GetString("tab-servers-title");
    public override string IconData => "M4,2 L20,2 A2,2 0 0 1 22,4 L22,8 A2,2 0 0 1 20,10 L4,10 A2,2 0 0 1 2,8 L2,4 A2,2 0 0 1 4,2 Z M4,14 L20,14 A2,2 0 0 1 22,16 L22,20 A2,2 0 0 1 20,22 L4,22 A2,2 0 0 1 2,20 L2,16 A2,2 0 0 1 4,14 Z M6,6 L6.01,6 M6,18 L6.01,18";
    public override string BadgeText => _serverListCache.AllServers.Count == 0
        ? string.Empty
        : _serverListCache.AllServers.Count(x => x.Status == ServerStatusCode.Online).ToString();

    public string? SearchString
    {
        get => _searchString;
        set
        {
            if (_searchString == value)
                return;

            OnPropertyChanging();
            _searchString = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearch));

            // Search string was changed, stop a potential old throttle timer and restart it
            _searchThrottle.Stop();
            _searchThrottle.Start();
        }
    }

    public string ResultCountText => $"Найдено: {SearchedServers.Count}";
    public string OnlineCountText => $"Онлайн: {_serverListCache.AllServers.Count(x => x.Status == ServerStatusCode.Online)}";
    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchString);
    public void ClearSearch() => SearchString = string.Empty;

    public ServerEntryViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (ReferenceEquals(_selectedServer, value)) return;
            if (_selectedServer != null) _selectedServer.IsExpanded = false;
            SetProperty(ref _selectedServer, value);
            if (_selectedServer != null) _selectedServer.IsExpanded = true;
            OnPropertyChanged(nameof(HasSelectedServer));
            OnPropertyChanged(nameof(SelectedStatusText));
        }
    }
    public bool HasSelectedServer => SelectedServer != null;
    public string SelectedStatusText => SelectedServer == null ? "Выберите сервер слева" : "Сервер выбран";
    public int ServerListLayout => Math.Clamp(_windowVm.Cfg.GetCVar(CVars.ServerListLayout), 1, 3);
    public bool IsTableLayout => ServerListLayout == 1;
    public bool IsSplitLayout => ServerListLayout == 2;
    public bool IsFocusLayout => ServerListLayout == 3;
    public bool ShowFocusList => IsFocusLayout && !IsFocusedDetailOpen;
    public bool ShowFocusDetail => IsFocusLayout && IsFocusedDetailOpen && SelectedServer != null;
    public int ServerLayoutPageIndex => IsSplitLayout ? 0 : IsTableLayout ? 1 : ShowFocusDetail ? 3 : 2;
    public bool IsFocusedDetailOpen
    {
        get => _isFocusedDetailOpen;
        private set
        {
            if (!SetProperty(ref _isFocusedDetailOpen, value)) return;
            OnPropertyChanged(nameof(ShowFocusList));
            OnPropertyChanged(nameof(ShowFocusDetail));
            OnPropertyChanged(nameof(ServerLayoutPageIndex));
        }
    }
    public string NameSortMark => SortMark(ServerSortMode.Name);
    public string RoundTimeSortMark => SortMark(ServerSortMode.RoundTime);
    public string PlayersSortMark => SortMark(ServerSortMode.Players);
    public string PingSortMark => SortMark(ServerSortMode.Ping);

    public void SortByName() => ApplySort(ServerSortMode.Name);
    public void SortByRoundTime() => ApplySort(ServerSortMode.RoundTime);
    public void SortByPlayers() => ApplySort(ServerSortMode.Players);
    public void SortByPing() => ApplySort(ServerSortMode.Ping);
    public void UseTableLayout() => SetServerListLayout(1);
    public void UseSplitLayout() => SetServerListLayout(2);
    public void UseFocusLayout() => SetServerListLayout(3);
    public void OpenSelectedServer()
    {
        if (IsFocusLayout && SelectedServer != null)
            IsFocusedDetailOpen = true;
    }
    public void BackToServerList()
    {
        IsFocusedDetailOpen = false;
        if (IsFocusLayout) SelectedServer = null;
    }

    public bool SpinnerVisible => _serverListCache.Status < RefreshListStatus.Updated;
    public void RequestSearchFocus() => SearchFocusRequested?.Invoke();
    public void ConnectCurrent()
    {
        if (SelectedServer is { CanConnect: true } selected)
            selected.ConnectPressed();
    }
    public void CloseExpanded()
    {
        if (ShowFocusDetail) BackToServerList();
        else SelectedServer = null;
    }

    public string ListText
    {
        get
        {
            var status = _serverListCache.Status;
            switch (status)
            {
                case RefreshListStatus.Error:
                    return _loc.GetString("tab-servers-list-status-error");
                case RefreshListStatus.PartialError:
                    return _loc.GetString("tab-servers-list-status-partial-error");
                case RefreshListStatus.UpdatingMaster:
                    return _loc.GetString("tab-servers-list-status-updating-master");
                case RefreshListStatus.NotUpdated:
                    return "";
                case RefreshListStatus.Updated:
                default:
                    if (SearchedServers.Count == 0 && _serverListCache.AllServers.Count != 0)
                        return _loc.GetString("tab-servers-list-status-none-filtered");

                    if (_serverListCache.AllServers.Count == 0)
                        return _loc.GetString("tab-servers-list-status-none");

                    return "";
            }
        }
    }

    [ObservableProperty] private bool _filtersVisible;
    public void ToggleFilters() => FiltersVisible = !FiltersVisible;

    public ServerListFiltersViewModel Filters { get; }

    public ServerListTabViewModel(MainWindowViewModel windowVm)
    {
        Filters = new ServerListFiltersViewModel(windowVm.Cfg, _loc);
        Filters.FiltersUpdated += FiltersOnFiltersUpdated;

        _windowVm = windowVm;
        _serverListCache = Locator.Current.GetRequiredService<ServerListCache>();

        _serverListCache.AllServers.CollectionChanged += ServerListUpdated;

        _serverListCache.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(ServerListCache.Status):
                    OnPropertyChanged(nameof(ListText));
                    OnPropertyChanged(nameof(SpinnerVisible));
                    break;
            }
        };

        _searchThrottle.Tick += (_, _) =>
        {
            // Interval since last search string change has passed, stop the timer and update the list
            _searchThrottle.Stop();
            UpdateSearchedList();
        };

        _loc.LanguageSwitched += () => Filters.UpdatePresentFilters(_serverListCache.AllServers);
    }

    private void FiltersOnFiltersUpdated()
    {
        UpdateSearchedList();
    }

    public override void Selected()
    {
        DiscordRichPresenceService.Instance.ShowSearching();
        _serverListCache.RequestInitialUpdate();
    }

    public void RefreshPressed()
    {
        _serverListCache.RequestRefresh();
        _windowVm.ShowToast("Список серверов обновляется");
    }

    private void ServerListUpdated(object? sender, NotifyCollectionChangedEventArgs notifyCollectionChangedEventArgs)
    {
        foreach (var server in _badgeServers)
            server.PropertyChanged -= BadgeServerPropertyChanged;
        _badgeServers.Clear();
        _badgeServers.AddRange(_serverListCache.AllServers);
        foreach (var server in _badgeServers)
            server.PropertyChanged += BadgeServerPropertyChanged;
        BadgeChanged();

        Filters.UpdatePresentFilters(_serverListCache.AllServers);

        UpdateSearchedList();
    }

    private void BadgeServerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerStatusData.Status))
            BadgeChanged();
    }

    private void UpdateSearchedList()
    {
        var selectedAddress = SelectedServer?.Address;
        var sortList = new List<ServerStatusData>();

        foreach (var server in _serverListCache.AllServers)
        {
            if (!DoesSearchMatch(server))
                continue;

            sortList.Add(server);
        }

        Filters.ApplyFilters(sortList);

        ApplySelectedSort(sortList);

        foreach (var oldEntry in SearchedServers)
            oldEntry.IsActive = false;

        var entries = sortList.Select(server =>
        {
            var entry = new ServerEntryViewModel(_windowVm, server, _serverListCache, _windowVm.Cfg)
            {
                SuppressIconOnActivation = !IsTableLayout
            };
            if (!IsTableLayout)
                entry.IsActive = true;
            return entry;
        }).ToArray();
        SearchedServers.SetItems(entries);
        if (IsFocusLayout && !IsFocusedDetailOpen)
            SelectedServer = null;
        else
            SelectedServer = entries.FirstOrDefault(x => string.Equals(x.Address, selectedAddress, StringComparison.OrdinalIgnoreCase))
                             ?? entries.FirstOrDefault();

        OnPropertyChanged(nameof(ListText));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(OnlineCountText));
    }

    private void SetServerListLayout(int layout)
    {
        layout = Math.Clamp(layout, 1, 3);
        if (ServerListLayout == layout) return;
        _windowVm.Cfg.SetCVar(CVars.ServerListLayout, layout);
        _windowVm.Cfg.CommitConfig();
        IsFocusedDetailOpen = false;
        OnPropertyChanged(nameof(ServerListLayout));
        OnPropertyChanged(nameof(IsTableLayout));
        OnPropertyChanged(nameof(IsSplitLayout));
        OnPropertyChanged(nameof(IsFocusLayout));
        OnPropertyChanged(nameof(ShowFocusList));
        OnPropertyChanged(nameof(ShowFocusDetail));
        OnPropertyChanged(nameof(ServerLayoutPageIndex));
        UpdateSearchedList();
    }

    private void ApplySort(ServerSortMode mode)
    {
        if (_sortMode == mode)
            _sortDescending = !_sortDescending;
        else
        {
            _sortMode = mode;
            _sortDescending = mode == ServerSortMode.Players;
        }

        OnPropertyChanged(nameof(NameSortMark));
        OnPropertyChanged(nameof(RoundTimeSortMark));
        OnPropertyChanged(nameof(PlayersSortMark));
        OnPropertyChanged(nameof(PingSortMark));
        UpdateSearchedList();
    }

    private void ApplySelectedSort(List<ServerStatusData> items)
    {
        var direction = _sortDescending ? -1 : 1;
        items.Sort((left, right) =>
        {
            var result = _sortMode switch
            {
                ServerSortMode.Name => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase),
                ServerSortMode.RoundTime => Nullable.Compare(RoundDuration(left), RoundDuration(right)),
                ServerSortMode.Players => left.PlayerCount.CompareTo(right.PlayerCount),
                ServerSortMode.Ping => Nullable.Compare(left.Ping, right.Ping),
                _ => 0
            };
            if (result == 0)
                result = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            return result * direction;
        });
    }

    private string SortMark(ServerSortMode mode) => _sortMode == mode ? (_sortDescending ? "↓" : "↑") : string.Empty;
    private static TimeSpan? RoundDuration(ServerStatusData server) =>
        server.RoundStartTime is { } start ? DateTime.UtcNow - start.ToUniversalTime() : null;

    private bool DoesSearchMatch(ServerStatusData data)
    {
        if (string.IsNullOrWhiteSpace(SearchString))
            return true;

        var query = SearchString.Trim();
        return Contains(data.Name) || Contains(data.Address) || Contains(data.Description) ||
               Contains(data.Map) || Contains(data.GamePreset) || data.Tags.Any(Contains);

        bool Contains(string? value) =>
            value?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    private enum ServerSortMode { Name, RoundTime, Players, Ping }
}
