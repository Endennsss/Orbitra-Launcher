using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DynamicData;
using DynamicData.Alias;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Splat;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.ServerStatus;
using SS14.Launcher.Utility;
using SS14.Launcher.Views;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public partial class HomePageViewModel : MainWindowTabViewModel
{
    public MainWindowViewModel MainWindowViewModel { get; }
    private readonly DataManager _cfg;
    private readonly ServerStatusCache _statusCache = new ServerStatusCache();
    private readonly ServerStatusCache _notificationStatusCache = new ServerStatusCache();
    private readonly ServerListCache _serverListCache;
    private readonly DispatcherTimer _favoriteRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3)
    };
    private readonly DispatcherTimer _notificationRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(12)
    };
    private readonly string _historyPath = Path.Combine(LauncherPaths.DirUserData, "recent-servers.json");
    public ObservableCollection<RecentServerViewModel> RecentServers { get; } = [];
    public bool HasRecentServers => RecentServers.Count > 0;
    private readonly Dictionary<string, FavoritePresenceState> _favoritePresence = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ServerStatusData> _notificationServers = new(StringComparer.OrdinalIgnoreCase);

    public HomePageViewModel(MainWindowViewModel mainWindowViewModel)
    {
        MainWindowViewModel = mainWindowViewModel;
        _cfg = Locator.Current.GetRequiredService<DataManager>();
        _serverListCache = Locator.Current.GetRequiredService<ServerListCache>();

        _cfg.FavoriteServers
            .Connect()
            .Select(x => new ServerEntryViewModel(MainWindowViewModel, _statusCache.GetStatusFor(x.Address), x, _statusCache, _cfg) { ViewedInFavoritesPane = true })
            .OnItemAdded(a =>
            {
                a.CacheData.PropertyChanged += FavoriteUiStatusChanged;
                if (IsSelected || _cfg.GetCVar(CVars.AutoRefreshFavoritePing))
                    _statusCache.InitialUpdateStatus(a.CacheData);

                var backgroundData = _notificationStatusCache.GetStatusFor(a.CacheData.Address);
                backgroundData.PropertyChanged += FavoriteStatusChanged;
                _notificationServers[a.CacheData.Address] = backgroundData;
                if (_cfg.IsFavoriteMonitored(a.CacheData.Address))
                    _notificationStatusCache.InitialUpdateStatus(backgroundData);
            })
            .OnItemRemoved(a =>
            {
                a.CacheData.PropertyChanged -= FavoriteUiStatusChanged;
                if (_notificationServers.Remove(a.CacheData.Address, out var backgroundData))
                    backgroundData.PropertyChanged -= FavoriteStatusChanged;
                _favoritePresence.Remove(a.CacheData.Address);
            })
            .AutoRefresh(x => x.CacheData.Status)
            .AutoRefresh(x => x.CacheData.Ping)
            .AutoRefresh(x => x.CacheData.PlayerCount)
            .Sort(Comparer<ServerEntryViewModel>.Create((a, b) => {
                var online = b.IsOnline.CompareTo(a.IsOnline);
                if (online != 0)
                    return online;
                var ping = a.CacheData.Ping is null
                    ? (b.CacheData.Ping is null ? 0 : 1)
                    : b.CacheData.Ping is null
                        ? -1
                        : a.CacheData.Ping.Value.CompareTo(b.CacheData.Ping.Value);
                if (ping != 0)
                    return ping;
                var players = b.CacheData.PlayerCount.CompareTo(a.CacheData.PlayerCount);
                if (players != 0)
                    return players;
                var dc = a.Favorite!.RaiseTime.CompareTo(b.Favorite!.RaiseTime);
                if (dc != 0)
                {
                    return -dc;
                }
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            }))
            .Bind(out var favorites)
            .Subscribe(_ =>
            {
                FavoritesEmpty = favorites.Count == 0;
                BadgeChanged();
            });

        Favorites = favorites;
        LoadRecentServers();

        _favoriteRefreshTimer.Tick += (_, _) =>
        {
            if (Favorites.Count > 0 && _cfg.GetCVar(CVars.AutoRefreshFavoritePing))
                _statusCache.Refresh();
        };
        _favoriteRefreshTimer.Start();

        _notificationRefreshTimer.Tick += (_, _) =>
        {
            if (Favorites.Count > 0 && _cfg.GetCVar(CVars.FavoriteNotificationsEnabled))
            {
                foreach (var favorite in Favorites.Where(f => _cfg.IsFavoriteMonitored(f.CacheData.Address)))
                    _notificationStatusCache.InitialUpdateStatus(_notificationServers[favorite.CacheData.Address]);
            }
        };
        _notificationRefreshTimer.Start();
    }

    public ReadOnlyObservableCollection<ServerEntryViewModel> Favorites { get; }

    [ObservableProperty] private bool _favoritesEmpty = true;

    public override string Name => LocalizationManager.Instance.GetString("tab-home-title");
    public override string IconData => "M3,10.5 L12,3 L21,10.5 M5,9.5 L5,21 L19,21 L19,9.5 M9,21 L9,14 L15,14 L15,21";
    public override string BadgeText => Favorites.Count(x => x.IsOnline) is var count && count > 0
        ? count.ToString()
        : string.Empty;
    public Control? Control { get; set; }

    public async void DirectConnectPressed()
    {
        if (!TryGetWindow(out var window))
        {
            return;
        }

        var res = await new DirectConnectDialog().ShowDialog<string?>(window);
        if (res == null)
        {
            return;
        }

        ConnectingViewModel.StartConnect(MainWindowViewModel, res);
        RecordRecentServer(res, res);
    }

    public async void AddFavoritePressed()
    {
        if (!TryGetWindow(out var window))
        {
            return;
        }

        var (name, address) = await new AddFavoriteDialog().ShowDialog<(string name, string address)>(window);

        try
        {
            _cfg.AddFavoriteServer(new FavoriteServer(name, address));
            _cfg.CommitConfig();
        }
        catch (ArgumentException)
        {
            // Happens if address already a favorite, so ignore.
            // TODO: Give a popup to the user?
        }
    }

    private bool TryGetWindow([NotNullWhen(true)] out Window? window)
    {
        window = Control?.GetVisualRoot() as Window;
        return window != null;
    }

    public void RefreshPressed()
    {
        _statusCache.Refresh();
        _serverListCache.RequestRefresh();
        MainWindowViewModel.ShowToast("Избранные серверы обновляются");
    }

    public override void Selected()
    {
        foreach (var favorite in Favorites)
        {
            _statusCache.InitialUpdateStatus(favorite.CacheData);
        }
        _serverListCache.RequestInitialUpdate();
    }

    private void FavoriteStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is ServerStatusData server &&
            _cfg.IsFavoriteMonitored(server.Address) &&
            e.PropertyName is nameof(ServerStatusData.Status)
                or nameof(ServerStatusData.PlayerCount)
                or nameof(ServerStatusData.SoftMaxPlayerCount)
                or nameof(ServerStatusData.RoundStartTime)
                or nameof(ServerStatusData.Map)
                or nameof(ServerStatusData.GamePreset))
        {
            if (_favoritePresence.TryGetValue(server.Address, out var previous))
            {
                if (_cfg.GetCVar(CVars.FavoriteNotifyServerOnline)
                    && previous.Status == ServerStatusCode.Offline && server.Status == ServerStatusCode.Online)
                    NotifyFavorite("Сервер снова доступен", server.Name ?? server.Address, server.Address);

                var wasFull = previous.MaxPlayers > 0 && previous.Players >= previous.MaxPlayers;
                var hasSlot = server.SoftMaxPlayerCount > 0 && server.PlayerCount < server.SoftMaxPlayerCount;
                if (_cfg.GetCVar(CVars.FavoriteNotifySlotAvailable) && wasFull && hasSlot)
                    NotifyFavorite("На сервере появилось место", server.Name ?? server.Address, server.Address);

                if (_cfg.GetCVar(CVars.FavoriteNotifyNewRound)
                    && previous.RoundStartTime is { } oldRound
                    && server.RoundStartTime is { } newRound
                    && newRound > oldRound.AddSeconds(5))
                {
                    var roundInfo = new[] { server.Map, server.GamePreset }
                        .Where(value => !string.IsNullOrWhiteSpace(value));
                    var suffix = string.Join(" · ", roundInfo);
                    NotifyFavorite("Начался новый раунд", string.IsNullOrEmpty(suffix)
                        ? server.Name ?? server.Address
                        : $"{server.Name ?? server.Address} · {suffix}", server.Address);
                }
            }

            _favoritePresence[server.Address] = new FavoritePresenceState(server.Status, server.PlayerCount,
                server.SoftMaxPlayerCount, server.RoundStartTime);
        }

    }

    public void SetFavoriteMonitoring(string address, bool enabled)
    {
        _cfg.SetFavoriteMonitored(address, enabled);
        var favorite = Favorites.FirstOrDefault(f =>
            string.Equals(f.CacheData.Address, address, StringComparison.OrdinalIgnoreCase));
        favorite?.RefreshTrayMonitoring();
        if (enabled && _notificationServers.TryGetValue(address, out var status))
            _notificationStatusCache.InitialUpdateStatus(status);
        else if (!enabled)
            _favoritePresence.Remove(address);
        MainWindowViewModel.ShowToast(enabled
            ? "Фоновое наблюдение за сервером включено"
            : "Фоновое наблюдение за сервером выключено");
    }

    private void FavoriteUiStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServerStatusData.Status))
            BadgeChanged();
    }

    private void NotifyFavorite(string title, string server, string address)
    {
        if (!_cfg.GetCVar(CVars.FavoriteNotificationsEnabled)
            || DiscordRichPresenceService.Instance.IsPlaying)
            return;

        MainWindowViewModel.ShowToast($"{title}: {server}");
        SystemNotificationService.Show(title, server,
            connect: () => ConnectFavorite(address),
            open: () => OpenFavorite(address),
            disable: () => SetFavoriteMonitoring(address, false));
        ActivityLog.Record("Сервер", title, server);
    }

    private void ConnectFavorite(string address)
    {
        var favorite = Favorites.FirstOrDefault(f => string.Equals(f.CacheData.Address, address, StringComparison.OrdinalIgnoreCase));
        favorite?.ConnectPressed();
    }

    private void OpenFavorite(string address)
    {
        if (MainWindowViewModel.Control is { } window)
        {
            window.ShowInTaskbar = true;
            window.Show();
            window.Activate();
        }
        MainWindowViewModel.SelectTabHome();
        var favorite = Favorites.FirstOrDefault(f => string.Equals(f.CacheData.Address, address, StringComparison.OrdinalIgnoreCase));
        if (favorite != null) favorite.IsExpanded = true;
    }

    public void RecordRecentServer(string name, string address)
    {
        PlaytimeTracker.SetServerName(address, name);
        var existing = RecentServers.FirstOrDefault(x =>
            string.Equals(x.Address, address, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            RecentServers.Remove(existing);
        RecentServers.Insert(0, new RecentServerViewModel(MainWindowViewModel, name, address, DateTimeOffset.Now));
        while (RecentServers.Count > 5)
            RecentServers.RemoveAt(RecentServers.Count - 1);
        OnPropertyChanged(nameof(HasRecentServers));
        SaveRecentServers();
    }

    private void LoadRecentServers()
    {
        try
        {
            if (!File.Exists(_historyPath))
                return;
            var entries = JsonSerializer.Deserialize<List<RecentServerData>>(File.ReadAllText(_historyPath)) ?? [];
            foreach (var entry in entries.Take(5))
                RecentServers.Add(new RecentServerViewModel(MainWindowViewModel, entry.Name, entry.Address, entry.LastConnected));
            OnPropertyChanged(nameof(HasRecentServers));
        }
        catch
        {
            // A damaged optional history file must not block launcher startup.
        }
    }

    private void SaveRecentServers()
    {
        try
        {
            var data = RecentServers.Select(x => new RecentServerData(x.Name, x.Address, x.LastConnected)).ToArray();
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(data));
        }
        catch
        {
            // History is optional.
        }
    }

    private sealed record RecentServerData(string Name, string Address, DateTimeOffset LastConnected);
    private sealed record FavoritePresenceState(ServerStatusCode Status, int Players, int MaxPlayers,
        DateTime? RoundStartTime);
}

public sealed class RecentServerViewModel(MainWindowViewModel window, string name, string address, DateTimeOffset lastConnected)
{
    public string Name { get; } = name;
    public string Address { get; } = address;
    public DateTimeOffset LastConnected { get; } = lastConnected;
    public string LastConnectedText => LastConnected.ToString("dd.MM HH:mm");

    public void ConnectPressed()
    {
        window.HomeTab.RecordRecentServer(Name, Address);
        ConnectingViewModel.StartConnect(window, Address);
    }
}
