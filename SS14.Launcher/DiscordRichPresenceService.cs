using System;
using System.Threading;
using System.Threading.Tasks;
using DiscordRPC;
using Serilog;
using Splat;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.ServerStatus;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

/// <summary>
/// Keeps Discord Rich Presence synchronized with launcher navigation.
/// Discord being closed or unavailable must never affect the launcher.
/// </summary>
public sealed class DiscordRichPresenceService : IDisposable
{
    private const string ApplicationId = "1532817045605978283";
    public static DiscordRichPresenceService Instance { get; } = new();

    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly object _sync = new();
    private readonly Timer _reconnectTimer;
    private readonly Timer _statsTimer;
    private readonly System.Net.Http.HttpClient _statusHttp = HappyEyeballsHttp.CreateHttpClient();
    private int _statsRefreshRunning;
    private DiscordRpcClient? _client;
    private RichPresence? _lastPresence;
    private bool _connected;
    private bool _disposed;
    private string? _selectedServerName;
    private string? _selectedServerAddress;
    private int _playerCount;
    private int _maxPlayerCount;
    private int? _pingMilliseconds;
    private string? _map;
    private string? _gamePreset;
    private bool _isPlaying;
    private string _smallImageKey = "player_avatar";
    private Guid? _avatarUserId;
    private DateTime _avatarCheckedAt;
    private int _avatarRefreshRunning;
    private string _launcherDetails = "В лаунчере";
    private string _launcherState = "Главное меню";
    public bool IsPlaying => _isPlaying;
    public bool IsConnected => _connected;
    private DataManager Config => Locator.Current.GetRequiredService<DataManager>();

    private DiscordRichPresenceService()
    {
        InitializeClient();
        // Discord is frequently started after the launcher or restarted during an update.
        // Periodically recreating a disconnected IPC client makes RPC recover without asking
        // the user to restart the launcher.
        _reconnectTimer = new Timer(_ => ReconnectIfNeeded(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        _statsTimer = new Timer(_ => _ = RefreshPlayingStatsAsync(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));
        ShowLauncher();
    }

    private async Task RefreshPlayingStatsAsync()
    {
        if (_disposed || !_isPlaying || string.IsNullOrWhiteSpace(_selectedServerAddress) ||
            Interlocked.Exchange(ref _statsRefreshRunning, 1) != 0)
            return;

        try
        {
            var data = new ServerStatusData(_selectedServerAddress);
            await ServerStatusCache.UpdateStatusFor(data, _statusHttp, CancellationToken.None);
            if (_disposed || !_isPlaying || data.Status != ServerStatusCode.Online ||
                !string.Equals(data.Address, _selectedServerAddress, StringComparison.OrdinalIgnoreCase))
                return;

            UpdateStats(data.PlayerCount, data.SoftMaxPlayerCount, data.Ping, data.Map, data.GamePreset);
            ShowPlaying();
        }
        catch (Exception e)
        {
            // A temporary server/API failure must keep the last known presence intact.
            Log.Debug(e, "Unable to refresh Discord RPC server statistics");
        }
        finally
        {
            Interlocked.Exchange(ref _statsRefreshRunning, 0);
        }
    }

    private void InitializeClient()
    {
        lock (_sync)
        {
            if (_disposed) return;
            try
            {
                _client?.Dispose();
                var client = new DiscordRpcClient(ApplicationId, pipe: -1, autoEvents: true)
                {
                    SkipIdenticalPresence = false
                };
                _connected = false;
                client.OnReady += (_, args) =>
                {
                    if (!ReferenceEquals(_client, client)) return;
                    _connected = true;
                    Log.Debug("Discord RPC ready for {User}", args.User.Username);
                    ResendPresence(client);
                };
                client.OnConnectionEstablished += (_, _) =>
                {
                    if (!ReferenceEquals(_client, client)) return;
                    _connected = true;
                    ResendPresence(client);
                };
                client.OnConnectionFailed += (_, _) =>
                {
                    if (ReferenceEquals(_client, client)) _connected = false;
                };
                client.OnClose += (_, _) =>
                {
                    if (ReferenceEquals(_client, client)) _connected = false;
                };
                client.OnError += (_, args) => Log.Debug("Discord RPC error: {Message}", args.Message);
                _client = client;
                client.Initialize();
            }
            catch (Exception e)
            {
                Log.Debug(e, "Discord RPC initialization failed");
                _connected = false;
                _client = null;
            }
        }
    }

    private void ReconnectIfNeeded()
    {
        try
        {
            if (_disposed || !Config.GetCVar(CVars.DiscordRpcEnabled) || _connected)
                return;
            Log.Debug("Discord RPC is disconnected; trying another IPC connection");
            InitializeClient();
        }
        catch (Exception e)
        {
            Log.Debug(e, "Discord RPC reconnect failed");
        }
    }

    private void ResendPresence(DiscordRpcClient client)
    {
        try
        {
            if (Config.GetCVar(CVars.DiscordRpcEnabled) && _lastPresence != null)
                client.SetPresence(_lastPresence);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Unable to restore Discord RPC presence");
        }
    }

    public void ShowLauncher()
    {
        _isPlaying = false;
        SetLauncherPresence("В лаунчере", "Главное меню");
    }

    public void ShowSearching()
    {
        _isPlaying = false;
        SetLauncherPresence("Ищет сервер", "Просматривает список серверов");
    }

    public void SelectServer(string name, string address, int playerCount, int maxPlayerCount, TimeSpan? ping,
        string? map, string? gamePreset)
    {
        _isPlaying = false;
        _selectedServerName = string.IsNullOrWhiteSpace(name) ? address : name;
        _selectedServerAddress = address;
        UpdateStats(playerCount, maxPlayerCount, ping, map, gamePreset);
        SetLauncherPresence("Выбрал сервер", _selectedServerName);
    }

    public void UpdateSelectedServerStats(string address, int playerCount, int maxPlayerCount, TimeSpan? ping,
        string? map, string? gamePreset)
    {
        if (!string.Equals(_selectedServerAddress, address, StringComparison.OrdinalIgnoreCase))
            return;

        UpdateStats(playerCount, maxPlayerCount, ping, map, gamePreset);
        if (_isPlaying)
            ShowPlaying();
    }

    public void ShowConnecting(string address)
    {
        if (!string.Equals(_selectedServerAddress, address, StringComparison.OrdinalIgnoreCase))
        {
            _selectedServerAddress = address;
            _selectedServerName = address;
        }

        _isPlaying = false;
        SetLauncherPresence("Подключается к серверу", _selectedServerName!);
    }

    public void ShowPlaying()
    {
        _isPlaying = true;
        var username = Locator.Current.GetService<LoginManager>()?.ActiveAccount?.Username ?? "Игрок";
        var online = _maxPlayerCount > 0 ? $"{_playerCount}/{_maxPlayerCount}" : _playerCount.ToString();
        var ping = _pingMilliseconds is { } value ? $"{value} мс" : "—";
        var details = Config.GetCVar(CVars.DiscordRpcShowServer)
            ? $"Играет: {_selectedServerName ?? "Space Station 14"}"
            : "Играет в Space Station 14";
        var parts = new System.Collections.Generic.List<string>();
        // Put round information first so it is retained even when Discord truncates a long state.
        if (Config.GetCVar(CVars.DiscordRpcShowMap) && !string.IsNullOrWhiteSpace(_map))
            parts.Add($"Карта: {_map}");
        if (Config.GetCVar(CVars.DiscordRpcShowGamePreset) && !string.IsNullOrWhiteSpace(_gamePreset))
            parts.Add($"Режим: {_gamePreset}");
        if (Config.GetCVar(CVars.DiscordRpcShowNickname)) parts.Add(username);
        if (Config.GetCVar(CVars.DiscordRpcShowOnline)) parts.Add($"Онлайн {online}");
        if (Config.GetCVar(CVars.DiscordRpcShowPing)) parts.Add($"Пинг {ping}");
        var smallImage = Config.GetCVar(CVars.DiscordRpcShowAvatar) ? GetAvatarImageKey() : null;
        SetPresence(details, parts.Count == 0 ? "В игре" : string.Join(" · ", parts), "in_the_game", smallImage,
            username);
    }

    public void RefreshSettings()
    {
        if (!Config.GetCVar(CVars.DiscordRpcEnabled))
        {
            _client?.ClearPresence();
            return;
        }
        if (_client == null || _client.IsDisposed)
            InitializeClient();
        if (_isPlaying) ShowPlaying(); else ShowLauncher();
    }

    public void RefreshProfileAvatar()
    {
        _avatarUserId = null;
        _avatarCheckedAt = DateTime.MinValue;
        RefreshSettings();
    }

    private void UpdateStats(int playerCount, int maxPlayerCount, TimeSpan? ping, string? map, string? gamePreset)
    {
        _playerCount = playerCount;
        _maxPlayerCount = maxPlayerCount;
        _pingMilliseconds = ping is { } value ? Math.Max(1, (int)Math.Round(value.TotalMilliseconds)) : null;
        _map = map;
        _gamePreset = gamePreset;
    }

    private string GetAvatarImageKey()
    {
        var account = Locator.Current.GetService<LoginManager>()?.ActiveAccount;
        if (account == null) return "player_avatar";
        if (_avatarUserId != account.UserId || DateTime.UtcNow - _avatarCheckedAt > TimeSpan.FromMinutes(2))
            _ = RefreshAvatarAsync(account.UserId);
        return _avatarUserId == account.UserId ? _smallImageKey : "player_avatar";
    }

    private void SetLauncherPresence(string details, string state)
    {
        _launcherDetails = details;
        _launcherState = state;
        var username = Locator.Current.GetService<LoginManager>()?.ActiveAccount?.Username ?? "Игрок";
        var smallImage = Config.GetCVar(CVars.DiscordRpcShowAvatar) ? GetAvatarImageKey() : null;
        SetPresence(details, state, "in_the_launcher", smallImage,
            username);
    }

    private async Task RefreshAvatarAsync(Guid userId)
    {
        if (Interlocked.Exchange(ref _avatarRefreshRunning, 1) != 0) return;
        try
        {
            using var social = new OrbitraSocialService();
            var profile = await social.GetProfileAsync(userId);
            _smallImageKey = social.AvatarUrl(profile?.AvatarPath) ?? "player_avatar";
            _avatarUserId = userId;
            _avatarCheckedAt = DateTime.UtcNow;
            if (_isPlaying) ShowPlaying(); else SetLauncherPresence(_launcherDetails, _launcherState);
        }
        catch (Exception e)
        {
            _smallImageKey = "player_avatar";
            _avatarUserId = userId;
            _avatarCheckedAt = DateTime.UtcNow;
            Log.Debug(e, "Unable to load Discord RPC profile avatar");
        }
        finally { Interlocked.Exchange(ref _avatarRefreshRunning, 0); }
    }

    private void SetPresence(string details, string state, string? largeImageKey = null,
        string? smallImageKey = null, string? smallImageText = null)
    {
        if (!Config.GetCVar(CVars.DiscordRpcEnabled))
        {
            _client?.ClearPresence();
            return;
        }
        if (_client is not { IsInitialized: true })
            return;

        try
        {
            var presence = new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = new Timestamps(_startedAt),
                Assets = largeImageKey == null
                    ? null
                    : new Assets
                    {
                        LargeImageKey = largeImageKey,
                        LargeImageText = largeImageKey == "in_the_game"
                            ? "Orbitra Launcher — в игре"
                            : "Orbitra Launcher",
                        SmallImageKey = smallImageKey,
                        SmallImageText = smallImageText
                    }
            };

            _lastPresence = presence;
            _client.SetPresence(presence);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Unable to update Discord RPC");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _reconnectTimer.Dispose();
            _statsTimer.Dispose();
            _statusHttp.Dispose();
            _client?.ClearPresence();
            _client?.Dispose();
            _client = null;
            _connected = false;
        }
    }
}
