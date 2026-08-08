using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Messaging;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.ServerStatus;
using static SS14.Launcher.Utility.HubUtility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class ServerEntryViewModel : ObservableRecipient, IRecipient<FavoritesChanged>, IViewModelBase
{
    private readonly LocalizationManager _loc = LocalizationManager.Instance;
    private readonly ServerStatusData _cacheData;
    private readonly IServerSource _serverSource;
    private readonly DataManager _cfg;
    private readonly MainWindowViewModel _windowVm;
    private string Address => _cacheData.Address;
    private string _fallbackName = string.Empty;
    private bool _isExpanded;
    private TimeSpan? _lastPing;
    private int? _lastPlayerCount;
    private bool _pingMetricUp;
    private bool _pingMetricDown;
    private bool _playerMetricUp;
    private bool _playerMetricDown;
    private Bitmap? _serverIcon;
    private readonly Queue<double> _pingHistory = new();
    private readonly Queue<double> _onlineHistory = new();

    public ServerEntryViewModel(MainWindowViewModel windowVm, ServerStatusData cacheData, IServerSource serverSource,
        DataManager cfg)
    {
        _cfg = cfg;
        _windowVm = windowVm;
        _cacheData = cacheData;
        _serverSource = serverSource;
    }

    public ServerEntryViewModel(
        MainWindowViewModel windowVm,
        ServerStatusData cacheData,
        FavoriteServer favorite,
        IServerSource serverSource,
        DataManager cfg)
        : this(windowVm, cacheData, serverSource, cfg)
    {
        Favorite = favorite;
    }

    public ServerEntryViewModel(
        MainWindowViewModel windowVm,
        ServerStatusDataWithFallbackName ssdfb,
        IServerSource serverSource,
        DataManager cfg)
        : this(windowVm, ssdfb.Data, serverSource, cfg)
    {
        FallbackName = ssdfb.FallbackName ?? "";
    }

    public void Tick()
    {
        OnPropertyChanged(nameof(RoundStartTime));
    }

    public void ConnectPressed()
    {
        UpdateDiscordSelection();
        _windowVm.HomeTab.RecordRecentServer(Name, Address);
        _windowVm.ShowToast($"Подключение к «{Name}»");
        ConnectingViewModel.StartConnect(_windowVm, Address);
    }

    public FavoriteServer? Favorite { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;

            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
            if (value)
                UpdateDiscordSelection();
            CheckUpdateInfo();
        }
    }

    public string Name => Favorite?.Name ?? _cacheData.Name ?? _fallbackName;

    private string FavoriteButtonText => IsFavorite
        ? _loc.GetString("server-entry-remove-favorite")
        : _loc.GetString("server-entry-add-favorite");

    public bool IsFavorite => _cfg.FavoriteServers.Lookup(Address).HasValue;
    public bool IsTrayMonitored => _cfg.IsFavoriteMonitored(Address);
    public string TrayMonitoringText => IsTrayMonitored ? "Отключить проверку в трее" : "Проверять в трее";

    public void ToggleTrayMonitoring()
    {
        _windowVm.HomeTab.SetFavoriteMonitoring(Address, !IsTrayMonitored);
    }

    public void RefreshTrayMonitoring()
    {
        OnPropertyChanged(nameof(IsTrayMonitored));
        OnPropertyChanged(nameof(TrayMonitoringText));
    }

    public bool ViewedInFavoritesPane { get; set; }

    public bool HaveData => _cacheData.Status == ServerStatusCode.Online;

    public string ServerStatusString
    {
        get
        {
            switch (_cacheData.Status)
            {
                case ServerStatusCode.Offline:
                    return _loc.GetString("server-entry-offline");
                case ServerStatusCode.FetchingStatus:
                case ServerStatusCode.Online:
                    return _loc.GetString("server-entry-fetching");
                default:
                    throw new NotSupportedException();
            }
        }
    }

    // Give a ratio for servers with a defined player count, or just a current number for those without.
    public string PlayerCountString =>
        _loc.GetString("server-entry-player-count",
            ("players", _cacheData.PlayerCount), ("max", _cacheData.SoftMaxPlayerCount));

    public string PingString => _cacheData.Ping is { } ping
        ? $"{Math.Max(1, (int)Math.Round(ping.TotalMilliseconds))} ms"
        : "—";

    public bool PingGood => _cacheData.Ping is { TotalMilliseconds: <= 80 };
    public bool PingMedium => _cacheData.Ping is { TotalMilliseconds: > 80 and <= 160 };
    public bool PingBad => _cacheData.Ping is { TotalMilliseconds: > 160 };


    public DateTime? RoundStartTime => _cacheData.RoundStartTime;

    public string RoundStatusString =>
        _cacheData.RoundStatus == GameRoundStatus.InLobby
            ? _loc.GetString("server-entry-status-lobby")
            : "";

    public string Description
    {
        get
        {
            switch (_cacheData.Status)
            {
                case ServerStatusCode.Offline:
                    return _loc.GetString("server-entry-description-offline");
                case ServerStatusCode.FetchingStatus:
                    return _loc.GetString("server-entry-description-fetching");
            }

            return _cacheData.StatusInfo switch
            {
                ServerStatusInfoCode.NotFetched => _loc.GetString("server-entry-description-fetching"),
                ServerStatusInfoCode.Fetching => _loc.GetString("server-entry-description-fetching"),
                ServerStatusInfoCode.Error => _loc.GetString("server-entry-description-error"),
                ServerStatusInfoCode.Fetched => _cacheData.Description ??
                                                _loc.GetString("server-entry-description-none"),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

    public bool IsOnline => _cacheData.Status == ServerStatusCode.Online;
    public bool IsConnecting => string.Equals(_windowVm.ConnectingVM?.TargetAddress, Address,
        StringComparison.OrdinalIgnoreCase);
    public bool IsConnectionSuccess => IsConnecting && _windowVm.ConnectingVM?.IsConnected == true;
    public bool IsConnectionBusy => IsConnecting && !IsConnectionSuccess;
    public bool CanConnect => IsOnline && !IsConnecting;
    public float ConnectionProgress => _windowVm.ConnectingVM?.Progress ?? 0;
    public bool ConnectionProgressIndeterminate => _windowVm.ConnectingVM?.ProgressIndeterminate ?? true;
    public string ConnectionStageText => _windowVm.ConnectingVM?.SmartStageText ?? "ПОДКЛЮЧЕНИЕ";
    public bool PingMetricUp => _pingMetricUp;
    public bool PingMetricDown => _pingMetricDown;
    public bool PlayerMetricUp => _playerMetricUp;
    public bool PlayerMetricDown => _playerMetricDown;
    public Bitmap? ServerIcon => _serverIcon;
    public bool HasServerIcon => _serverIcon != null;
    public Geometry PingGraph => BuildGraph(_pingHistory, 160, 38);
    public Geometry OnlineGraph => BuildGraph(_onlineHistory, 160, 38);
    public string PlaytimeText
    {
        get
        {
            var time = PlaytimeTracker.Get(Address);
            if (time.TotalHours >= 1) return $"{(int)time.TotalHours} ч {time.Minutes} мин";
            return time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} мин" : "меньше минуты";
        }
    }

    public async void CopyAddressPressed()
    {
        var clipboard = TopLevel.GetTopLevel(_windowVm.Control)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(Address);
        _windowVm.ShowToast("Адрес сервера скопирован");
    }

    public void CreateDesktopShortcut()
    {
        if (!OperatingSystem.IsWindows()) { _windowVm.ShowToast("Ярлыки поддерживаются только в Windows", true); return; }
        try
        {
            var invalid = Path.GetInvalidFileNameChars();
            var safeName = new string(Name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Space Station 14 server";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), safeName + ".url");
            var icon = Environment.ProcessPath ?? "";
            File.WriteAllText(path, $"[InternetShortcut]\r\nURL={OrbitraProtocol.CreateInvite(Address)}\r\nIconFile={icon}\r\nIconIndex=0\r\n");
            _windowVm.ShowToast("Ярлык сервера создан на рабочем столе");
        }
        catch (Exception e) { _windowVm.ShowToast($"Не удалось создать ярлык: {e.Message}", true); }
    }

    public void OpenWebsitePressed()
    {
        if (!UriHelper.TryParseSs14Uri(Address, out var address))
            return;
        Helpers.OpenUri(UriHelper.GetServerApiAddress(address));
    }

    public string FallbackName
    {
        get => _fallbackName;
        set
        {
            SetProperty(ref _fallbackName, value);
            OnPropertyChanged(nameof(Name));
        }
    }

    public ServerStatusData CacheData => _cacheData;

    public string? FetchedFrom
    {
        get
        {
            if (_cfg.HasCustomHubs)
            {
                return _cacheData.HubAddress == null
                    ? null
                    : _loc.GetString("server-fetched-from-hub", ("hub", GetHubShortName(_cacheData.HubAddress)));
            }

            return null;
        }
    }

    public bool ShowFetchedFrom => _cfg.HasCustomHubs && !ViewedInFavoritesPane;

    public void FavoriteButtonPressed()
    {
        if (IsFavorite)
        {
            // Remove favorite.
            _cfg.RemoveFavoriteServer(_cfg.FavoriteServers.Lookup(Address).Value);
        }
        else
        {
            var fav = new FavoriteServer(_cacheData.Name ?? FallbackName, Address);
            _cfg.AddFavoriteServer(fav);
        }

        _cfg.CommitConfig();
        _windowVm.ShowToast(IsFavorite ? "Сервер добавлен в избранное" : "Сервер удалён из избранного");
    }

    public void FavoriteRaiseButtonPressed()
    {
        if (IsFavorite)
        {
            // Usual business, raise priority
            _cfg.RaiseFavoriteServer(_cfg.FavoriteServers.Lookup(Address).Value);
        }

        _cfg.CommitConfig();
    }

    public void Receive(FavoritesChanged message)
    {
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteButtonText));
    }

    private void CheckUpdateInfo()
    {
        if (!IsExpanded || _cacheData.Status != ServerStatusCode.Online)
            return;

        if (_cacheData.StatusInfo is not (ServerStatusInfoCode.NotFetched or ServerStatusInfoCode.Error))
            return;

        _serverSource.UpdateInfoFor(_cacheData);
    }

    protected override void OnActivated()
    {
        base.OnActivated();

        _cacheData.PropertyChanged += OnCacheDataOnPropertyChanged;
        _windowVm.PropertyChanged += OnWindowViewModelPropertyChanged;
        PlaytimeTracker.Changed += OnPlaytimeChanged;
        LoadServerIcon();
    }

    protected override void OnDeactivated()
    {
        base.OnDeactivated();

        _cacheData.PropertyChanged -= OnCacheDataOnPropertyChanged;
        _windowVm.PropertyChanged -= OnWindowViewModelPropertyChanged;
        PlaytimeTracker.Changed -= OnPlaytimeChanged;
    }

    private async void LoadServerIcon()
    {
        if (_serverIcon != null)
            return;
        _serverIcon = await ServerIconCache.GetAsync(Address);
        OnPropertyChanged(nameof(ServerIcon));
        OnPropertyChanged(nameof(HasServerIcon));
    }

    private void OnWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainWindowViewModel.ConnectingVM))
            return;

        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsConnectionSuccess));
        OnPropertyChanged(nameof(IsConnectionBusy));
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(ConnectionProgress));
        OnPropertyChanged(nameof(ConnectionProgressIndeterminate));
        OnPropertyChanged(nameof(ConnectionStageText));

        if (_windowVm.ConnectingVM != null)
            _windowVm.ConnectingVM.PropertyChanged += OnConnectingPropertyChanged;
    }

    private void OnConnectingPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ConnectingViewModel.Progress) or nameof(ConnectingViewModel.ProgressIndeterminate)
            or nameof(ConnectingViewModel.SmartStageText) or nameof(ConnectingViewModel.StatusText))
        {
            OnPropertyChanged(nameof(ConnectionProgress));
            OnPropertyChanged(nameof(ConnectionProgressIndeterminate));
            OnPropertyChanged(nameof(ConnectionStageText));
        }
        else if (args.PropertyName == nameof(ConnectingViewModel.IsConnected))
        {
            OnPropertyChanged(nameof(IsConnectionSuccess));
            OnPropertyChanged(nameof(IsConnectionBusy));
        }
    }

    private void OnCacheDataOnPropertyChanged(object? _, PropertyChangedEventArgs args)
    {
        switch (args.PropertyName)
        {
            case nameof(IServerStatusData.PlayerCount):
                AddGraphPoint(_onlineHistory, _cacheData.PlayerCount);
                OnPropertyChanged(nameof(OnlineGraph));
                if (_lastPlayerCount is { } oldPlayers && oldPlayers != _cacheData.PlayerCount)
                    FlashPlayerMetric(_cacheData.PlayerCount > oldPlayers);
                _lastPlayerCount = _cacheData.PlayerCount;
                OnPropertyChanged(nameof(ServerStatusString));
                OnPropertyChanged(nameof(PlayerCountString));
                break;
            case nameof(IServerStatusData.SoftMaxPlayerCount):
                OnPropertyChanged(nameof(ServerStatusString));
                OnPropertyChanged(nameof(PlayerCountString));
                break;

            case nameof(IServerStatusData.RoundStartTime):
                OnPropertyChanged(nameof(RoundStartTime));
                break;

            case nameof(ServerStatusData.Ping):
                if (_cacheData.Ping is { } graphPing)
                    AddGraphPoint(_pingHistory, graphPing.TotalMilliseconds);
                OnPropertyChanged(nameof(PingGraph));
                if (_lastPing is { } oldPing && _cacheData.Ping is { } newPing && oldPing != newPing)
                    FlashPingMetric(newPing < oldPing);
                _lastPing = _cacheData.Ping;
                OnPropertyChanged(nameof(PingString));
                OnPropertyChanged(nameof(PingGood));
                OnPropertyChanged(nameof(PingMedium));
                OnPropertyChanged(nameof(PingBad));
                break;

            case nameof(IServerStatusData.RoundStatus):
                OnPropertyChanged(nameof(RoundStatusString));
                break;

            case nameof(IServerStatusData.Status):
                OnPropertyChanged(nameof(IsOnline));
                OnPropertyChanged(nameof(CanConnect));
                OnPropertyChanged(nameof(ServerStatusString));
                OnPropertyChanged(nameof(PlayerCountString));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(HaveData));
                CheckUpdateInfo();
                break;

            case nameof(IServerStatusData.Name):
                OnPropertyChanged(nameof(Name));
                if (IsExpanded)
                    UpdateDiscordSelection();
                break;

            case nameof(IServerStatusData.Description):
            case nameof(IServerStatusData.StatusInfo):
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(HaveData));
                break;
        }

        if (args.PropertyName is nameof(IServerStatusData.PlayerCount)
            or nameof(IServerStatusData.SoftMaxPlayerCount)
            or nameof(ServerStatusData.Ping)
            or nameof(IServerStatusData.Map)
            or nameof(IServerStatusData.GamePreset))
        {
            DiscordRichPresenceService.Instance.UpdateSelectedServerStats(Address, _cacheData.PlayerCount,
                _cacheData.SoftMaxPlayerCount, _cacheData.Ping, _cacheData.Map, _cacheData.GamePreset);
        }
    }

    private void OnPlaytimeChanged(string address)
    {
        if (string.Equals(address, Address, StringComparison.OrdinalIgnoreCase))
            OnPropertyChanged(nameof(PlaytimeText));
    }

    private static void AddGraphPoint(Queue<double> values, double value)
    {
        values.Enqueue(value);
        while (values.Count > 30) values.Dequeue();
    }

    private static Geometry BuildGraph(Queue<double> values, double width, double height)
    {
        if (values.Count == 0) return Geometry.Parse("M0,38 L160,38");
        var points = values.ToArray();
        var min = points.Min();
        var max = points.Max();
        var range = Math.Max(1, max - min);
        var step = points.Length == 1 ? width : width / (points.Length - 1);
        var path = new System.Text.StringBuilder();
        for (var i = 0; i < points.Length; i++)
        {
            var x = i * step;
            var y = height - 3 - (points[i] - min) / range * (height - 6);
            path.Append(i == 0 ? 'M' : 'L').Append(x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
        }
        return Geometry.Parse(path.ToString());
    }

    private async void FlashPingMetric(bool improved)
    {
        _pingMetricUp = improved;
        _pingMetricDown = !improved;
        OnPropertyChanged(nameof(PingMetricUp));
        OnPropertyChanged(nameof(PingMetricDown));
        await Task.Delay(650);
        _pingMetricUp = _pingMetricDown = false;
        OnPropertyChanged(nameof(PingMetricUp));
        OnPropertyChanged(nameof(PingMetricDown));
    }

    private async void FlashPlayerMetric(bool increased)
    {
        _playerMetricUp = increased;
        _playerMetricDown = !increased;
        OnPropertyChanged(nameof(PlayerMetricUp));
        OnPropertyChanged(nameof(PlayerMetricDown));
        await Task.Delay(650);
        _playerMetricUp = _playerMetricDown = false;
        OnPropertyChanged(nameof(PlayerMetricUp));
        OnPropertyChanged(nameof(PlayerMetricDown));
    }

    private void UpdateDiscordSelection()
    {
        DiscordRichPresenceService.Instance.SelectServer(Name, Address, _cacheData.PlayerCount,
            _cacheData.SoftMaxPlayerCount, _cacheData.Ping, _cacheData.Map, _cacheData.GamePreset);
    }
}
