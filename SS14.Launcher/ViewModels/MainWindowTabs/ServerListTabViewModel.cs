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
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public partial class ServerListTabViewModel : MainWindowTabViewModel
{
    private readonly LocalizationManager _loc = LocalizationManager.Instance;
    private readonly MainWindowViewModel _windowVm;
    private readonly ServerListCache _serverListCache;
    private readonly List<ServerStatusData> _badgeServers = [];

    public ObservableList<ServerEntryViewModel> SearchedServers { get; } = [];
    public event Action? SearchFocusRequested;

    private string? _searchString;
    private readonly DispatcherTimer _searchThrottle = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private readonly DispatcherTimer _quietPingTimer = new() { Interval = TimeSpan.FromSeconds(5) };

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

    public bool SpinnerVisible => _serverListCache.Status < RefreshListStatus.Updated;
    public void RequestSearchFocus() => SearchFocusRequested?.Invoke();
    public void ConnectCurrent() => SearchedServers.FirstOrDefault(x => x.IsExpanded && x.CanConnect)?.ConnectPressed();
    public void CloseExpanded()
    {
        foreach (var entry in SearchedServers.Where(x => x.IsExpanded))
            entry.IsExpanded = false;
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

        _quietPingTimer.Tick += async (_, _) =>
        {
            if (SearchedServers.Count != 0)
                await _serverListCache.RefreshPingsQuietlyAsync(SearchedServers.Select(x => x.CacheData));
        };
        _quietPingTimer.Start();

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
        var sortList = new List<ServerStatusData>();

        foreach (var server in _serverListCache.AllServers)
        {
            if (!DoesSearchMatch(server))
                continue;

            sortList.Add(server);
        }

        Filters.ApplyFilters(sortList);

        sortList.Sort(ServerSortComparer.Instance);

        SearchedServers.SetItems(sortList.Select(server
            => new ServerEntryViewModel(_windowVm, server, _serverListCache, _windowVm.Cfg)));

        OnPropertyChanged(nameof(ListText));
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(OnlineCountText));
    }

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

    private sealed class ServerSortComparer : NotNullComparer<ServerStatusData>
    {
        public static readonly ServerSortComparer Instance = new();

        public override int Compare(ServerStatusData x, ServerStatusData y)
        {
            // Sort by player count descending.
            var res = x.PlayerCount.CompareTo(y.PlayerCount);
            if (res != 0)
                return -res;

            // Sort by name.
            res = string.Compare(x.Name, y.Name, StringComparison.CurrentCultureIgnoreCase);
            if (res != 0)
                return res;

            // Sort by address.
            return string.Compare(x.Address, y.Address, StringComparison.Ordinal);
        }
    }
}
