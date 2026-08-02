using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using JetBrains.Annotations;
using Serilog;
using Splat;
using SS14.Launcher.Localization;
using SS14.Launcher.Models;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Models.OverrideAssets;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels;
using SS14.Launcher.Views;

namespace SS14.Launcher;

public class App : Application
{
    private static readonly Dictionary<string, AssetDef> AssetDefs = new()
    {
        ["WindowIcon"] = new AssetDef("icon.ico", AssetType.WindowIcon),
        ["OrbitraLogo"] = new AssetDef("orbitra-logo.png", AssetType.Bitmap),
        ["LogoLong"] = new AssetDef("logo-long.png", AssetType.Bitmap),
    };

    private readonly OverrideAssetsManager _overrideAssets;

    private readonly Dictionary<string, object> _baseAssets = new();
    private TrayIcon? _trayIcon;
    private NativeMenu? _trayMenu;
    private MainWindow? _mainWindow;
    private MainWindowViewModel? _mainWindowViewModel;

    // XAML insists on a parameterless constructor existing, despite this never being used.
    [UsedImplicitly]
    public App()
    {
        throw new InvalidOperationException();
    }

    public App(OverrideAssetsManager overrideAssets)
    {
        _overrideAssets = overrideAssets;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        var cfg = Locator.Current.GetRequiredService<Models.Data.DataManager>();
        ApplyLauncherFont(cfg.GetCVar(Models.Data.CVars.LauncherFont));
        if (Program.SafeModeActive) ApplyColorTheme(false); else ApplyConfiguredTheme(cfg);

        LoadBaseAssets();
        IconsLoader.Load(this);
        UiSoundService.Initialize();

        _overrideAssets.AssetsChanged += OnAssetsChanged;
    }

    public static void ApplyLauncherFont(string fontName)
    {
        if (Current == null)
            return;

        // Emoji are selected through Unicode-scoped fallbacks in Program.cs. Keeping
        // only the text family here prevents Noto Emoji from taking over digits.
        var family = fontName == "Noto Sans"
            ? new FontFamily("avares://SS14.Launcher/Assets/Fonts/noto_sans/*.ttf#Noto Sans")
            : new FontFamily(fontName);
        Current.Resources["LauncherFontFamily"] = family;
    }

    public static void ApplyColorTheme(bool useLightTheme)
    {
        if (Current == null)
            return;

        Current.RequestedThemeVariant = useLightTheme ? ThemeVariant.Light : ThemeVariant.Dark;

        var palette = useLightTheme
            ? new Dictionary<string, string>
            {
                ["ThemeBackgroundBrush"] = "#F3F3F3",
                ["ThemePopupBackgroundBrush"] = "#FFFFFF",
                ["ThemeForegroundBrush"] = "#181818",
                ["ThemeForegroundMutedBrush"] = "#666666",
                ["ThemeControlMidBrush"] = "#E2E2E2",
                ["ThemeControlHighBrush"] = "#282828",
                ["ThemeNanoGoldBrush"] = "#181818",
                ["ThemeSubTextBrush"] = "#666666",
                ["ThemeButtonHoveredBrush"] = "#D8D8D8",
                ["ThemeStripebackEdgeBrush"] = "#D0D0D0",
                ["ThemeTabItemSelectedBrush"] = "#D6D6D6",
                ["ThemeTabItemHoveredBrush"] = "#E4E4E4",
                ["LauncherWorkspaceBrush"] = "#EEEEEE",
                ["LauncherChromeBrush"] = "#E5E5E5",
                ["LauncherSurfaceBrush"] = "#FAFAFA",
                ["LauncherSurfaceAltBrush"] = "#F0F0F0",
                ["LauncherControlBrush"] = "#E2E2E2",
                ["LauncherHoverBrush"] = "#D7D7D7",
                ["LauncherLineBrush"] = "#C5C5C5",
                ["LauncherTextBrush"] = "#181818",
                ["LauncherMutedBrush"] = "#686868",
                ["HighlightBrush"] = "#242424",
                ["ThemeAccentBrush"] = "#242424",
                ["ThemeBorderMidBrush"] = "#B8B8B8",
                ["ThemeBorderHighBrush"] = "#666666",
                ["WindowOverlayBrush"] = "#66000000",
            }
            : new Dictionary<string, string>
            {
                ["ThemeBackgroundBrush"] = "#101010",
                ["ThemePopupBackgroundBrush"] = "#191919",
                ["ThemeForegroundBrush"] = "#F2F2F2",
                ["ThemeForegroundMutedBrush"] = "#777777",
                ["ThemeControlMidBrush"] = "#292929",
                ["ThemeControlHighBrush"] = "#E8E8E8",
                ["ThemeNanoGoldBrush"] = "#F2F2F2",
                ["ThemeSubTextBrush"] = "#A0A0A0",
                ["ThemeButtonHoveredBrush"] = "#393939",
                ["ThemeStripebackEdgeBrush"] = "#393939",
                ["ThemeTabItemSelectedBrush"] = "#303030",
                ["ThemeTabItemHoveredBrush"] = "#232323",
                ["LauncherWorkspaceBrush"] = "#0D0D0D",
                ["LauncherChromeBrush"] = "#121212",
                ["LauncherSurfaceBrush"] = "#171717",
                ["LauncherSurfaceAltBrush"] = "#1D1D1D",
                ["LauncherControlBrush"] = "#292929",
                ["LauncherHoverBrush"] = "#393939",
                ["LauncherLineBrush"] = "#3B3B3B",
                ["LauncherTextBrush"] = "#F2F2F2",
                ["LauncherMutedBrush"] = "#A0A0A0",
                ["HighlightBrush"] = "#F0F0F0",
                ["ThemeAccentBrush"] = "#F0F0F0",
                ["ThemeBorderMidBrush"] = "#4A4A4A",
                ["ThemeBorderHighBrush"] = "#A0A0A0",
                ["WindowOverlayBrush"] = "#AA000000",
            };

        foreach (var (key, color) in palette)
            Current.Resources[key] = new SolidColorBrush(Color.Parse(color));

        Current.Resources["ThemeForegroundColor"] = Color.Parse(useLightTheme ? "#181818" : "#F2F2F2");
        Current.Resources["ThemeListSeparatorColor"] = Color.Parse(useLightTheme ? "#AAC5C5C5" : "#AA383838");
        Current.Resources["ThemeListSeparatorColorTransparent"] = Color.Parse(useLightTheme ? "#00C5C5C5" : "#00383838");
        Current.Resources["ThemeStripeBackBrush"] = new SolidColorBrush(
            Color.Parse(useLightTheme ? "#EEEEEE" : "#0D0D0D"));
    }

    public static void ApplyConfiguredTheme(DataManager cfg)
    {
        if (!cfg.GetCVar(CVars.CustomThemeEnabled))
        {
            ApplyColorTheme(cfg.GetCVar(CVars.LightTheme));
            return;
        }

        ApplyColorTheme(false);
        if (Current == null) return;
        string Safe(CVarDef<string> cvar, string fallback)
        {
            try { return Color.Parse(cfg.GetCVar(cvar)).ToString(); }
            catch { return fallback; }
        }
        var background = Safe(CVars.CustomThemeBackground, "#101010");
        var surface = Safe(CVars.CustomThemeSurface, "#181818");
        var control = Safe(CVars.CustomThemeControl, "#292929");
        var accent = Safe(CVars.CustomThemeAccent, "#D0D0D0");
        var text = Safe(CVars.CustomThemeText, "#F2F2F2");
        var muted = Safe(CVars.CustomThemeMuted, "#999999");
        var palette = new Dictionary<string, string>
        {
            ["ThemeBackgroundBrush"] = background, ["ThemePopupBackgroundBrush"] = surface,
            ["ThemeForegroundBrush"] = text, ["ThemeForegroundMutedBrush"] = muted,
            ["ThemeSubTextBrush"] = muted, ["LauncherWorkspaceBrush"] = background,
            ["LauncherChromeBrush"] = surface, ["LauncherSurfaceBrush"] = surface,
            ["LauncherSurfaceAltBrush"] = control, ["LauncherControlBrush"] = control,
            ["LauncherHoverBrush"] = accent, ["LauncherLineBrush"] = muted,
            ["LauncherTextBrush"] = text, ["LauncherMutedBrush"] = muted,
            ["HighlightBrush"] = accent, ["ThemeAccentBrush"] = accent,
            ["ThemeButtonHoveredBrush"] = accent, ["ThemeTabItemSelectedBrush"] = control,
            ["ThemeTabItemHoveredBrush"] = surface,
        };
        foreach (var (key, color) in palette)
            Current.Resources[key] = new SolidColorBrush(Color.Parse(color));
        Current.Resources["ThemeStripeBackBrush"] = new SolidColorBrush(Color.Parse(background));
        Current.Resources["ThemeForegroundColor"] = Color.Parse(text);
    }

    private void LoadBaseAssets()
    {
        foreach (var (name, (path, type)) in AssetDefs)
        {
            using var dataStream = AssetLoader.Open(new Uri($"avares://SS14.Launcher/Assets/{path}"));

            var asset = LoadAsset(type, dataStream);

            _baseAssets.Add(name, asset);
            Resources.Add(name, asset);
        }
    }

    private void OnAssetsChanged(OverrideAssetsChanged obj)
    {
        foreach (var (name, data) in obj.Files)
        {
            // Project branding is fixed: remote seasonal assets may customize artwork,
            // but must never replace the executable/window/taskbar icon.
            if (name == "WindowIcon")
                continue;

            if (!AssetDefs.TryGetValue(name, out var def))
            {
                Log.Warning("Unable to find asset def for asset: '{AssetName}'", name);
                continue;
            }

            var ms = new MemoryStream(data, writable: false);
            var asset = LoadAsset(def.Type, ms);

            Resources[name] = asset;
        }

        // Clear assets not given to base data.
        foreach (var (name, asset) in _baseAssets)
        {
            if (!obj.Files.ContainsKey(name))
                Resources[name] = asset;
        }
    }

    private static object LoadAsset(AssetType type, Stream data)
    {
        return type switch
        {
            AssetType.Bitmap => new Bitmap(data),
            AssetType.WindowIcon => new WindowIcon(data),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private sealed record AssetDef(string DefaultPath, AssetType Type);

    private enum AssetType
    {
        Bitmap,
        WindowIcon
    }

    // Called when Avalonia init is done
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Startup += OnStartup;
            desktop.Exit += OnExit;
        }
    }

    private void OnStartup(object? s, ControlledApplicationLifetimeStartupEventArgs e)
    {
        var loc = Locator.Current.GetRequiredService<LocalizationManager>();
        var msgr = Locator.Current.GetRequiredService<LauncherMessaging>();
        var contentManager = Locator.Current.GetRequiredService<ContentManager>();
        var overrideAssets = Locator.Current.GetRequiredService<OverrideAssetsManager>();
        var launcherInfo = Locator.Current.GetRequiredService<LauncherInfoManager>();

        loc.Initialize();
        launcherInfo.Initialize();
        contentManager.Initialize();
        overrideAssets.Initialize();

        var viewModel = new MainWindowViewModel();
        var window = new MainWindow
        {
            DataContext = viewModel
        };
        _mainWindow = window;
        _mainWindowViewModel = viewModel;
        viewModel.OnWindowInitialized();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            InitializeTray(desktop);

        loc.LanguageSwitched += () =>
        {
            window.ReloadContent();

            // Reloading content isn't a smooth process anyway, so let's do some housekeeping while we're at it.
            GC.Collect();
        };

        var lc = new LauncherCommands(viewModel);
        lc.RunCommandTask();
        Locator.CurrentMutable.RegisterConstant(lc);
        msgr.StartServerTask(lc);

        if (ConfigConstants.IsAuthOverride)
        {
            Log.Information("Auth URL override detected: {AuthUrl}.", ConfigConstants.AuthUrl);
            viewModel.ShouldShowAuthOverrideWarning = true;
            viewModel.StartAuthOverrideCountdown();
        }

        window.Show();
    }

    private void InitializeTray(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _trayMenu = new NativeMenu();
        _trayMenu.NeedsUpdate += (_, _) => RebuildTrayMenu(desktop);

        _trayIcon = new TrayIcon
        {
            Icon = Resources["WindowIcon"] as WindowIcon,
            ToolTipText = "Orbitra Launcher",
            Menu = _trayMenu,
            IsVisible = true,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        RebuildTrayMenu(desktop);
    }

    private void RebuildTrayMenu(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_trayMenu == null)
            return;

        _trayMenu.Items.Clear();

        var showItem = new NativeMenuItem("Открыть лаунчер");
        showItem.Click += (_, _) => ShowMainWindow();
        _trayMenu.Add(showItem);
        _trayMenu.Add(new NativeMenuItemSeparator());

        var accountMenu = new NativeMenu();
        var loginManager = Locator.Current.GetRequiredService<LoginManager>();
        var accounts = loginManager.Logins.Items.OrderBy(account => account.Username).ToArray();
        if (accounts.Length == 0)
        {
            accountMenu.Add(new NativeMenuItem("Нет сохранённых аккаунтов") { IsEnabled = false });
        }
        else
        {
            foreach (var account in accounts)
            {
                var captured = account;
                var active = ReferenceEquals(loginManager.ActiveAccount, captured);
                var item = new NativeMenuItem($"{(active ? "✓ " : string.Empty)}{captured.Username}");
                item.Click += (_, _) =>
                {
                    ShowMainWindow();
                    _mainWindowViewModel?.TrySwitchToAccount(captured);
                    RebuildTrayMenu(desktop);
                };
                accountMenu.Add(item);
            }
        }
        _trayMenu.Add(new NativeMenuItem("Сменить аккаунт") { Menu = accountMenu });
        _trayMenu.Add(new NativeMenuItemSeparator());

        var favorites = Locator.Current.GetRequiredService<DataManager>().FavoriteServers.Items
            .OrderByDescending(favorite => favorite.RaiseTime)
            .ToArray();

        if (favorites.Length == 0)
        {
            _trayMenu.Add(new NativeMenuItem("Избранных серверов пока нет") { IsEnabled = false });
        }
        else
        {
            foreach (var favorite in favorites)
            {
                var captured = favorite;
                var monitored = Locator.Current.GetRequiredService<DataManager>().IsFavoriteMonitored(captured.Address);
                var item = new NativeMenuItem($"{(monitored ? "✓ " : string.Empty)}{(string.IsNullOrWhiteSpace(captured.Name)
                    ? captured.Address
                    : captured.Name)}");
                var serverMenu = new NativeMenu();
                var connectItem = new NativeMenuItem("Подключиться");
                connectItem.Click += (_, _) => ConnectFavorite(captured);
                serverMenu.Add(connectItem);
                var monitorItem = new NativeMenuItem(monitored ? "Не проверять в фоне" : "Проверять в фоне");
                monitorItem.Click += (_, _) =>
                {
                    if (_mainWindowViewModel != null)
                        _mainWindowViewModel.HomeTab.SetFavoriteMonitoring(captured.Address, !monitored);
                    RebuildTrayMenu(desktop);
                };
                serverMenu.Add(monitorItem);
                item.Menu = serverMenu;
                _trayMenu.Add(item);
            }
        }

        _trayMenu.Add(new NativeMenuItemSeparator());
        var exitItem = new NativeMenuItem("Выйти");
        exitItem.Click += (_, _) =>
        {
            _mainWindow?.PrepareForExit();
            desktop.Shutdown();
        };
        _trayMenu.Add(exitItem);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.ShowInTaskbar = true;
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ConnectFavorite(FavoriteServer favorite)
    {
        if (_mainWindowViewModel == null)
            return;

        ShowMainWindow();
        if (_mainWindowViewModel.ConnectingVM != null)
        {
            _mainWindowViewModel.ShowToast("Подключение уже выполняется", true);
            return;
        }

        var name = string.IsNullOrWhiteSpace(favorite.Name) ? favorite.Address : favorite.Name;
        DiscordRichPresenceService.Instance.SelectServer(name, favorite.Address, 0, 0, null, null, null);
        _mainWindowViewModel.HomeTab.RecordRecentServer(name, favorite.Address);
        _mainWindowViewModel.ShowToast($"Подключение к «{name}»");
        ConnectingViewModel.StartConnect(_mainWindowViewModel, favorite.Address);
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        PlaytimeTracker.Stop();
        _trayIcon?.Dispose();
        _trayIcon = null;
        var msgr = Locator.Current.GetRequiredService<LauncherMessaging>();
        msgr.StopAndWait();
    }
}
