using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;
using AnimatedImage.Avalonia;
using Avalonia.Threading;
using DynamicData;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Serilog;
using Splat;
using SS14.Launcher.Api;
using SS14.Launcher.Localization;
using SS14.Launcher.Models;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels.Login;
using SS14.Launcher.ViewModels.MainWindowTabs;
using SS14.Launcher.Views;

namespace SS14.Launcher.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IErrorOverlayOwner
{
    public ObservableCollection<LauncherToast> Toasts { get; } = [];
    public ObservableCollection<CommandPaletteItem> CommandPaletteItems { get; } = [];
    private readonly DataManager _cfg;
    private readonly LoginManager _loginMgr;
    private readonly LauncherInfoManager _infoManager;
    private readonly LocalizationManager _loc;

    private int _selectedIndex;
    private readonly List<MainWindowTabViewModel> _allTabs = [];

    public DataManager Cfg => _cfg;
    public LoggedInAccount? ActiveAccount => _loginMgr.ActiveAccount;
    public ICVarEntry<bool> UseTextLogo => Cfg.GetCVarEntry(CVars.UseTextLogo);
    [ObservableProperty] private bool _outOfDate;
    [ObservableProperty] private bool _customUpdateAvailable;
    [ObservableProperty] private string _customUpdateVersion = string.Empty;
    [ObservableProperty] private bool _customUpdateInstalling;
    [ObservableProperty] private double _customUpdateProgress;
    [ObservableProperty] private string _customUpdateStatus = string.Empty;
    private string? _customUpdateUrl;
    private CustomReleaseAssetDto? _customUpdateAsset;

    private IDisposable? _authOverrideCountdownTimer;

    public HomePageViewModel HomeTab { get; }
    public ServerListTabViewModel ServersTab { get; }
    public NewsTabViewModel NewsTab { get; }
    public UsefulLinksTabViewModel UsefulLinksTab { get; }
    public OptionsTabViewModel OptionsTab { get; }
    public CustomThemeTabViewModel CustomThemeTab { get; }
    public PlaytimeTabViewModel PlaytimeTab { get; }
    public ProfileTabViewModel ProfileTab { get; }
    public ActivityTabViewModel ActivityTab { get; }
    public SystemCenterTabViewModel SystemCenterTab { get; }

    public MainWindowViewModel()
    {
        _cfg = Locator.Current.GetRequiredService<DataManager>();
        _loginMgr = Locator.Current.GetRequiredService<LoginManager>();
        _infoManager = Locator.Current.GetRequiredService<LauncherInfoManager>();
        _loc = LocalizationManager.Instance;
        if (!Program.SafeModeActive) DiscordRichPresenceService.Instance.ShowLauncher();

        AccountDropDown = new AccountDropDownViewModel(this);

        ServersTab = new ServerListTabViewModel(this);
        NewsTab = new NewsTabViewModel(this);
        UsefulLinksTab = new UsefulLinksTabViewModel();
        HomeTab = new HomePageViewModel(this);
        OptionsTab = new OptionsTabViewModel(this);
        CustomThemeTab = new CustomThemeTabViewModel(this);
        PlaytimeTab = new PlaytimeTabViewModel();
        ProfileTab = new ProfileTabViewModel(this);
        ActivityTab = new ActivityTabViewModel();
        SystemCenterTab = new SystemCenterTabViewModel(this);

        _allTabs.AddRange([
            HomeTab,
            ServersTab,
            NewsTab,
            UsefulLinksTab,
            PlaytimeTab,
            ProfileTab,
            ActivityTab,
            SystemCenterTab,
            CustomThemeTab,
            OptionsTab,
#if DEVELOPMENT
            new DevelopmentTabViewModel(this),
#endif
        ]);
        ApplySavedNavigationOrder();
        RefreshVisibleTabs();
        OptionsTab.InitializeNavigation();

        LoginViewModel = new MainWindowLoginViewModel();

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LoggedIn) && LoggedIn)
            {
                RunSelectedOnTab();
                OnPropertyChanged(nameof(ShowFirstRun));
            }
        };

        _loginMgr.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(_loginMgr.ActiveAccount))
            {
                OnPropertyChanged(new PropertyChangedEventArgs(nameof(LoggedIn)));
                OrbitraProtocol.PublishPresence(null);
            }
        };

        _cfg.Logins.Connect()
            .Subscribe(_ => OnPropertyChanged(new PropertyChangedEventArgs(nameof(AccountDropDownVisible))));

        BuildCommandPalette();
    }

    public MainWindow? Control { get; set; }

    public ObservableCollection<MainWindowTabViewModel> Tabs { get; } = [];
    public IReadOnlyList<MainWindowTabViewModel> AllTabs => _allTabs;

    public string GetNavigationId(MainWindowTabViewModel tab)
    {
        if (ReferenceEquals(tab, HomeTab)) return "home";
        if (ReferenceEquals(tab, ServersTab)) return "servers";
        if (ReferenceEquals(tab, NewsTab)) return "news";
        if (ReferenceEquals(tab, UsefulLinksTab)) return "links";
        if (ReferenceEquals(tab, OptionsTab)) return "options";
        if (ReferenceEquals(tab, CustomThemeTab)) return "custom-theme";
        if (ReferenceEquals(tab, PlaytimeTab)) return "playtime";
        if (ReferenceEquals(tab, ProfileTab)) return "profile";
        if (ReferenceEquals(tab, ActivityTab)) return "activity";
        if (ReferenceEquals(tab, SystemCenterTab)) return "system-center";
        return "development";
    }

    public bool CanHideNavigationTab(string id) => id is not "home" and not "options";

    public bool IsNavigationTabVisible(string id) =>
        !ParseCsv(_cfg.GetCVar(CVars.HiddenNavigationTabs)).Contains(id) || !CanHideNavigationTab(id);

    public void SetNavigationTabVisible(string id, bool visible)
    {
        if (!CanHideNavigationTab(id)) return;
        var hidden = ParseCsv(_cfg.GetCVar(CVars.HiddenNavigationTabs));
        if (visible) hidden.Remove(id); else hidden.Add(id);
        _cfg.SetCVar(CVars.HiddenNavigationTabs, string.Join(',', hidden));
        _cfg.CommitConfig();
        RefreshVisibleTabs();
    }

    public void MoveNavigationTab(string id, int direction)
    {
        var index = _allTabs.FindIndex(tab => GetNavigationId(tab) == id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _allTabs.Count) return;
        (_allTabs[index], _allTabs[target]) = (_allTabs[target], _allTabs[index]);
        _cfg.SetCVar(CVars.NavigationTabOrder, string.Join(',', _allTabs.Select(GetNavigationId)));
        _cfg.CommitConfig();
        RefreshVisibleTabs();
    }

    private void ApplySavedNavigationOrder()
    {
        if (_cfg.GetCVar(CVars.NavigationOrderVersion) < 3)
        {
            _cfg.SetCVar(CVars.NavigationTabOrder,
                "home,servers,news,links,playtime,profile,activity,system-center,custom-theme,options,development");
            _cfg.SetCVar(CVars.NavigationOrderVersion, 3);
            _cfg.CommitConfig();
        }
        var order = ParseCsv(_cfg.GetCVar(CVars.NavigationTabOrder)).ToList();
        _allTabs.Sort((a, b) =>
        {
            var ai = order.IndexOf(GetNavigationId(a));
            var bi = order.IndexOf(GetNavigationId(b));
            return (ai < 0 ? int.MaxValue : ai).CompareTo(bi < 0 ? int.MaxValue : bi);
        });
    }

    private void RefreshVisibleTabs()
    {
        var selected = Tabs.Count > 0 && _selectedIndex >= 0 && _selectedIndex < Tabs.Count
            ? Tabs[_selectedIndex]
            : HomeTab;
        Tabs.Clear();
        foreach (var tab in _allTabs.Where(tab => IsNavigationTabVisible(GetNavigationId(tab))))
            Tabs.Add(tab);
        _selectedIndex = Math.Max(0, Tabs.IndexOf(selected));
        RunSelectedOnTab();
    }

    private static HashSet<string> ParseCsv(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool LoggedIn => _loginMgr.ActiveAccount != null;
    public bool AccountDropDownVisible => _loginMgr.Logins.Count != 0;
    public bool ShowFirstRun => LoggedIn && !_cfg.GetCVar(CVars.FirstRunCompleted);

    public AccountDropDownViewModel AccountDropDown { get; }

    public MainWindowLoginViewModel LoginViewModel { get; }

    [ObservableProperty] private ConnectingViewModel? _connectingVM;

    [ObservableProperty] private string? _busyTask;
    [ObservableProperty] private ViewModelBase? _overlayViewModel;
    [ObservableProperty] private bool _commandPaletteOpen;
    private string _commandPaletteQuery = string.Empty;
    public string CommandPaletteQuery
    {
        get => _commandPaletteQuery;
        set { _commandPaletteQuery = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(FilteredCommandPaletteItems)); }
    }
    public IEnumerable<CommandPaletteItem> FilteredCommandPaletteItems => CommandPaletteItems.Where(item =>
        string.IsNullOrWhiteSpace(CommandPaletteQuery) || item.Title.Contains(CommandPaletteQuery, StringComparison.OrdinalIgnoreCase));

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var previous = Tabs[_selectedIndex];
            previous.IsSelected = false;

            if (!EqualityComparer<int>.Default.Equals(_selectedIndex, value))
            {
                OnPropertyChanging();
                _selectedIndex = value;
                OnPropertyChanged();
            }

            RunSelectedOnTab();
            OnPropertyChanged(nameof(ActiveThemeBackground));
            OnPropertyChanged(nameof(ActiveAnimatedThemeBackground));
        }
    }

    private void RunSelectedOnTab()
    {
        var tab = Tabs[_selectedIndex];
        tab.IsSelected = true;
        tab.Selected();
    }

    public ICVarEntry<bool> HasDismissedEarlyAccessWarning => Cfg.GetCVarEntry(CVars.HasDismissedEarlyAccessWarning);
    public bool ShouldShowIntelDegradationWarning => IsVulnerableToIntelDegradation(_cfg);
    public bool ShouldShowRosettaWarning => IsAppleSiliconInRosetta(_cfg);
    [ObservableProperty] private bool _shouldShowAuthOverrideWarning;
    [ObservableProperty] private int _authOverrideCountdown = 5;
    [ObservableProperty] private bool _isAuthOverrideButtonEnabled;

    public string Version => $"v{LauncherVersion.Version}";

    public async void OnWindowInitialized()
    {
        BusyTask = _loc.GetString("main-window-busy-checking-update");
        await CheckLauncherUpdate();
        BusyTask = _loc.GetString("main-window-busy-checking-login-status");
        await CheckAccounts();
        BusyTask = null;

        if (_cfg.SelectedLoginId is { } g && _loginMgr.Logins.TryLookup(g, out var login))
        {
            TrySwitchToAccount(login);
        }

        if (Program.SafeModeActive)
            ShowToast("Включён безопасный режим после некорректного завершения", true);
        await CheckCustomLauncherUpdate();

        // We should now start reacting to commands.
    }

    private async Task CheckAccounts()
    {
        // Check if accounts are still valid and refresh their tokens if necessary.
        await _loginMgr.Initialize();
    }

    public void OnDiscordButtonPressed()
    {
        Helpers.OpenUri(new Uri(ConfigConstants.DiscordUrl));
    }

    public void OnWebsiteButtonPressed()
    {
        Helpers.OpenUri(new Uri(ConfigConstants.WebsiteUrl));
    }

    public Bitmap? ActiveThemeBackground
    {
        get
        {
            if (Tabs.Count == 0 || _selectedIndex < 0 || _selectedIndex >= Tabs.Count) return null;
            return CustomThemeTab.GetBackgroundFor(GetNavigationId(Tabs[_selectedIndex]));
        }

    }

    public AnimatedImageSource? ActiveAnimatedThemeBackground
    {
        get
        {
            if (Tabs.Count == 0 || _selectedIndex < 0 || _selectedIndex >= Tabs.Count) return null;
            var path = CustomThemeTab.GetBackgroundPathFor(GetNavigationId(Tabs[_selectedIndex]));
            return CustomThemeTabViewModel.IsAnimatedImage(path) && path != null
                ? new AnimatedImageSourceUri(new Uri(path))
                : null;
        }
    }

    public void RefreshThemeVisuals()
    {
        OnPropertyChanged(nameof(ActiveThemeBackground));
        OnPropertyChanged(nameof(ActiveAnimatedThemeBackground));
    }

    public async void ShowToast(string message, bool isError = false)
    {
        if (isError)
            UiSoundService.PlayError();
        var toast = new LauncherToast(message, isError);
        Toasts.Add(toast);
        await Task.Delay(2800);
        Toasts.Remove(toast);
    }

    public void OnChemHelperButtonPressed()
    {
        Helpers.OpenUri(new Uri(ConfigConstants.ChemHelperUrl));
    }

    private async Task CheckLauncherUpdate()
    {
        // await Task.Delay(1000);
        if (!ConfigConstants.DoVersionCheck)
        {
            return;
        }

        await _infoManager.LoadTask;
        if (_infoManager.Model == null)
        {
            // Error while loading.
            Log.Warning("Unable to check for launcher update due to error, assuming up-to-date.");
            OutOfDate = false;
            return;
        }

        OutOfDate = Array.IndexOf(_infoManager.Model.AllowedVersions, ConfigConstants.CurrentLauncherVersion) == -1;
        Log.Debug("Launcher out of date? {Value}", OutOfDate);
    }

    public void ExitPressed()
    {
        Control?.Close();
    }

    public void DownloadPressed()
    {
        Helpers.OpenUri(new Uri(ConfigConstants.DownloadUrl));
    }

    public void DismissEarlyAccessPressed()
    {
        Cfg.SetCVar(CVars.HasDismissedEarlyAccessWarning, true);
        Cfg.CommitConfig();
    }

    public void DismissIntelDegradationPressed()
    {
        Cfg.SetCVar(CVars.HasDismissedIntelDegradation, true);
        Cfg.CommitConfig();
        OnPropertyChanged(nameof(ShouldShowIntelDegradationWarning));
    }

    public void DismissAppleSiliconRosettaPressed()
    {
        Cfg.SetCVar(CVars.HasDismissedRosettaWarning, true);
        Cfg.CommitConfig();
        OnPropertyChanged(nameof(ShouldShowRosettaWarning));
    }

    public void DismissAuthOverridePressed()
    {
        _authOverrideCountdownTimer?.Dispose();
        _authOverrideCountdownTimer = null;
        ShouldShowAuthOverrideWarning = false;
    }

    public void StartAuthOverrideCountdown()
    {
        AuthOverrideCountdown = 5;
        IsAuthOverrideButtonEnabled = false;
        _authOverrideCountdownTimer?.Dispose();

        _authOverrideCountdownTimer = DispatcherTimer.Run(() =>
        {
            AuthOverrideCountdown--;
            if (AuthOverrideCountdown <= 0)
            {
                IsAuthOverrideButtonEnabled = true;
                _authOverrideCountdownTimer?.Dispose();
                _authOverrideCountdownTimer = null;
                return false;
            }
            return true;
        }, TimeSpan.FromSeconds(1), DispatcherPriority.Normal);
    }

    public void SelectTabServers()
    {
        SelectedIndex = Tabs.IndexOf(ServersTab);
    }

    private async Task CheckCustomLauncherUpdate(bool manual = false)
    {
        if (!manual && !_cfg.GetCVar(CVars.CustomUpdateChecks)) return;
        try
        {
            if (manual)
            {
                CustomUpdateAvailable = false;
                ShowToast("Проверяем обновления Orbitra Launcher…");
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Orbitra-Launcher");
            var json = await client.GetStringAsync(ConfigConstants.CustomLatestReleaseApiUrl);
            var release = JsonSerializer.Deserialize<CustomReleaseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (release == null || release.Draft || release.Prerelease)
            {
                if (manual) ShowToast("Стабильных обновлений не найдено");
                return;
            }
            var tag = release.TagName.TrimStart('v', 'V');
            if (!System.Version.TryParse(tag.Split('-', 2)[0], out var latest) || LauncherVersion.Version == null)
            {
                if (manual) ShowToast("Не удалось определить версию релиза", true);
                return;
            }
            if (latest <= LauncherVersion.Version)
            {
                if (manual) ShowToast($"Установлена актуальная версия {LauncherVersion.Version}");
                return;
            }
            _customUpdateUrl = release.HtmlUrl;
            _customUpdateAsset = release.Assets.FirstOrDefault(asset =>
                asset.Name.Equals("Orbitra_Launcher_Windows.zip", StringComparison.OrdinalIgnoreCase));
            CustomUpdateVersion = release.TagName;
            CustomUpdateAvailable = true;
            if (manual) ShowToast($"Доступно обновление {release.TagName}");
        }
        catch (Exception e)
        {
            Log.Debug(e, "Unable to check custom launcher release");
            if (manual) ShowToast("Не удалось проверить обновления · проверьте интернет", true);
        }
    }

    public async void CheckCustomLauncherUpdateManually() => await CheckCustomLauncherUpdate(true);

    public void OpenCustomUpdate()
    {
        if (Uri.TryCreate(_customUpdateUrl, UriKind.Absolute, out var uri)) Helpers.OpenUri(uri);
    }

    public void DismissCustomUpdate() => CustomUpdateAvailable = false;

    public async void InstallCustomUpdate()
    {
        if (_customUpdateAsset == null || CustomUpdateInstalling) return;
        try
        {
            CustomUpdateInstalling = true;
            CustomUpdateStatus = "Скачивание и проверка обновления…";
            await LauncherUpdateService.StageAndRestartAsync(CustomUpdateVersion,
                _customUpdateAsset.BrowserDownloadUrl, _customUpdateAsset.Digest,
                value => Avalonia.Threading.Dispatcher.UIThread.Post(() => CustomUpdateProgress = value));
            CustomUpdateStatus = "Обновление готово · перезапуск…";
            Control?.PrepareForExit();
            Control?.Close();
        }
        catch (Exception e)
        {
            Log.Error(e, "Unable to install launcher update");
            CustomUpdateStatus = e.Message;
            ShowToast($"Не удалось установить обновление: {e.Message}", true);
            CustomUpdateInstalling = false;
        }
    }

    private sealed class CustomReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;
        public bool Draft { get; init; }
        public bool Prerelease { get; init; }
        public CustomReleaseAssetDto[] Assets { get; init; } = [];
    }

    private sealed class CustomReleaseAssetDto
    {
        public string Name { get; init; } = string.Empty;
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
        public string Digest { get; init; } = string.Empty;
    }

    public void SelectTabHome()
    {
        var index = Tabs.IndexOf(HomeTab);
        if (index >= 0) SelectedIndex = index;
    }

    public void RefreshCurrentTab()
    {
        if (Tabs[SelectedIndex] == ServersTab)
            ServersTab.RefreshPressed();
        else if (Tabs[SelectedIndex] == HomeTab)
            HomeTab.RefreshPressed();
    }

    public void ConnectCurrentServer()
    {
        if (Tabs[SelectedIndex] == ServersTab)
            ServersTab.ConnectCurrent();
        else if (Tabs[SelectedIndex] == HomeTab)
            HomeTab.Favorites.FirstOrDefault(x => x.IsExpanded && x.CanConnect)?.ConnectPressed();
    }

    public void CloseExpandedServers()
    {
        ServersTab.CloseExpanded();
        foreach (var server in HomeTab.Favorites.Where(x => x.IsExpanded))
            server.IsExpanded = false;
    }

    public void ToggleCommandPalette()
    {
        CommandPaletteOpen = !CommandPaletteOpen;
        if (CommandPaletteOpen) CommandPaletteQuery = string.Empty;
    }

    public void CloseCommandPalette() => CommandPaletteOpen = false;

    private void BuildCommandPalette()
    {
        void Add(string title, Action action) => CommandPaletteItems.Add(new CommandPaletteItem(this, title, action));
        Add("Перейти: Главная", SelectTabHome);
        Add("Перейти: Серверы", SelectTabServers);
        Add("Перейти: Новости", () => SelectTab(NewsTab));
        Add("Перейти: Профиль", () => SelectTab(ProfileTab));
        Add("Перейти: Настройки", () => SelectTab(OptionsTab));
        Add("Обновить текущую вкладку", RefreshCurrentTab);
        Add("Открыть папку логов", OptionsTab.OpenLogDirectory);
        Add("Экспортировать настройки", OptionsTab.ExportSettingsBackup);
    }

    private void SelectTab(MainWindowTabViewModel tab)
    {
        var index = Tabs.IndexOf(tab); if (index >= 0) SelectedIndex = index;
    }

    public void CompleteFirstRun()
    {
        _cfg.SetCVar(CVars.FirstRunCompleted, true);
        _cfg.CommitConfig();
        OnPropertyChanged(nameof(ShowFirstRun));
        ShowToast("Настройка лаунчера завершена");
    }

    public void TrySwitchToAccount(LoggedInAccount account)
    {
        switch (account.Status)
        {
            case AccountLoginStatus.Unsure:
                TrySelectUnsureAccount(account);
                break;

            case AccountLoginStatus.Available:
                _loginMgr.ActiveAccount = account;
                break;

            case AccountLoginStatus.Expired:
                _loginMgr.ActiveAccount = null;
                LoginViewModel.SwitchToExpiredLogin(account);
                break;
        }
    }

    private async void TrySelectUnsureAccount(LoggedInAccount account)
    {
        BusyTask = _loc.GetString("main-window-busy-checking-account-status");
        try
        {
            await _loginMgr.UpdateSingleAccountStatus(account);

            // Can't be unsure, that'd have thrown.
            Debug.Assert(account.Status != AccountLoginStatus.Unsure);
            TrySwitchToAccount(account);
        }
        catch (AuthApiException e)
        {
            Log.Warning(e, "AuthApiException while trying to refresh account {login}", account.LoginInfo);
            OverlayViewModel = new AuthErrorsOverlayViewModel(this, _loc.GetString("main-window-error-connecting-auth-server"),
                new[]
                {
                    e.InnerException?.Message ?? _loc.GetString("main-window-error-unknown")
                });
        }
        finally
        {
            BusyTask = null;
        }
    }

    public void OverlayOk()
    {
        OverlayViewModel = null;
    }

    public bool IsContentBundleDropValid(IStorageFile file)
    {
        // Can only load content bundles if logged in, in some capacity.
        if (!LoggedIn)
            return false;

        // Disallow if currently connecting to a server.
        if (ConnectingVM != null)
            return false;

        return Path.GetExtension(file.Name) == ".zip";
    }

    public void Dropped(IStorageFile file)
    {
        // Trust view validated this.
        Debug.Assert(IsContentBundleDropValid(file));

        ConnectingViewModel.StartContentBundle(this, file);
    }

    private static bool IsVulnerableToIntelDegradation(DataManager cfg)
    {
        var processor = LauncherDiagnostics.GetProcessorModel();

        // No Intel processor, or already dismissed the warning.
        if (!processor.Contains("Intel") || cfg.GetCVar(CVars.HasDismissedIntelDegradation))
            return false;

        // Get the i#-#### from the processor string.
        var match = Regex.Match(processor, @"i\d+-\d+(?:[A-Z]+)?(?=\s|$)");
        if (!match.Success)
            return false;

        var affectedGenerations = new[] { "i3-13", "i5-13", "i7-13", "i9-13", "i3-14", "i5-14", "i7-14", "i9-14" };
        var excludedSuffixes = new[] { "HX", "H", "P", "U" };

        return affectedGenerations.Any(match.Value.Contains) && !excludedSuffixes.Any(match.Value.EndsWith);
    }

    private static bool IsAppleSiliconInRosetta(DataManager cfg)
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        var processor = LauncherDiagnostics.GetProcessorModel();

        return processor.Contains("VirtualApple") && !cfg.GetCVar(CVars.HasDismissedRosettaWarning);
    }
}

public sealed class CommandPaletteItem
{
    private readonly MainWindowViewModel _owner;
    private readonly Action _action;
    public string Title { get; }
    public CommandPaletteItem(MainWindowViewModel owner, string title, Action action) { _owner = owner; Title = title; _action = action; }
    public void Invoke() { _action(); _owner.CloseCommandPalette(); }
}

public sealed record LauncherToast(string Message, bool IsError);
