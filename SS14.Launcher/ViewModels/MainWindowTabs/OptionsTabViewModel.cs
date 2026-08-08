using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using Avalonia.Platform.Storage;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Splat;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.EngineManager;
using SS14.Launcher.Utility;
using SS14.Launcher.ViewModels;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public class OptionsTabViewModel : MainWindowTabViewModel
{
    public DataManager Cfg { get; }
    private readonly IEngineManager _engineManager;
    private readonly ContentManager _contentManager;
    private readonly MainWindowViewModel? _mainWindow;
    private int _selectedSettingsSection;
    public ObservableCollection<NavigationTabOptionViewModel> NavigationTabs { get; } = [];

    public LanguageSelectorViewModel Language { get; } = new();
    public AccountDropDownViewModel? AccountDropDown { get; }
    public CustomThemeTabViewModel? CustomThemeTab => _mainWindow?.CustomThemeTab;
    public ActivityTabViewModel? ActivityTab => _mainWindow?.ActivityTab;
    public SystemCenterTabViewModel? SystemCenterTab => _mainWindow?.SystemCenterTab;
    public DevelopmentTabViewModel? DevelopmentTab => _mainWindow?.DevelopmentTab;
    public bool DevelopmentAvailable => DevelopmentTab != null;
    public int SelectedSettingsSection
    {
        get => _selectedSettingsSection;
        set
        {
            if (!SetProperty(ref _selectedSettingsSection, value)) return;
            if (value == 2) ActivityTab?.Selected();
            if (value == 3) SystemCenterTab?.Selected();
            if (value == 4) DevelopmentTab?.Selected();
        }
    }

    public OptionsTabViewModel() : this(null)
    {
    }

    public OptionsTabViewModel(MainWindowViewModel? mainWindow)
    {
        Cfg = Locator.Current.GetRequiredService<DataManager>();
        _engineManager = Locator.Current.GetRequiredService<IEngineManager>();
        _contentManager = Locator.Current.GetRequiredService<ContentManager>();
        AccountDropDown = mainWindow?.AccountDropDown;
        _mainWindow = mainWindow;

        DisableIncompatibleMacOS = OperatingSystem.IsMacOS();
    }
    public bool DisableIncompatibleMacOS { get; }
    public IReadOnlyList<string> AvailableFonts { get; } =
        ["Noto Sans", "Segoe UI", "Arial", "Consolas"];
    public bool UseTextLogo
    {
        get => Cfg.GetCVar(CVars.UseTextLogo);
        set
        {
            Cfg.SetCVar(CVars.UseTextLogo, value);
            Cfg.CommitConfig();
        }
    }

    public void InitializeNavigation()
    {
        NavigationTabs.Clear();
        if (_mainWindow == null) return;
        foreach (var tab in _mainWindow.AllTabs)
            NavigationTabs.Add(new NavigationTabOptionViewModel(this, _mainWindow, tab));
    }

    public string SelectedFont
    {
        get => Cfg.GetCVar(CVars.LauncherFont);
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            Cfg.SetCVar(CVars.LauncherFont, value);
            Cfg.CommitConfig();
            App.ApplyLauncherFont(value);
        }
    }

    public bool LightTheme
    {
        get => Cfg.GetCVar(CVars.LightTheme);
        set
        {
            if (!CanUseLightTheme) return;
            Cfg.SetCVar(CVars.LightTheme, value);
            Cfg.CommitConfig();
            App.ApplyColorTheme(value);
        }
    }
    public bool CanUseLightTheme => !Cfg.GetCVar(CVars.CustomThemeEnabled);
    public void RefreshThemeAvailability()
    {
        OnPropertyChanged(nameof(CanUseLightTheme));
        OnPropertyChanged(nameof(LightTheme));
    }

    public bool UiSoundsEnabled
    {
        get => Cfg.GetCVar(CVars.UiSoundsEnabled);
        set
        {
            Cfg.SetCVar(CVars.UiSoundsEnabled, value);
            Cfg.CommitConfig();
        }
    }

    public int UiSoundVolume
    {
        get => Cfg.GetCVar(CVars.UiSoundVolume);
        set
        {
            var volume = Math.Clamp(value, 0, 100);
            if (volume == Cfg.GetCVar(CVars.UiSoundVolume))
                return;
            Cfg.SetCVar(CVars.UiSoundVolume, volume);
            Cfg.CommitConfig();
            OnPropertyChanged(nameof(UiSoundVolume));
        }
    }

    public bool CloseToTray
    {
        get => Cfg.GetCVar(CVars.CloseToTray);
        set { Cfg.SetCVar(CVars.CloseToTray, value); Cfg.CommitConfig(); }
    }

    public IReadOnlyList<DownloadLimitOption> DownloadLimits { get; } =
    [
        new(0, "Без ограничения"), new(512, "512 КБ/с"), new(1024, "1 МБ/с"),
        new(2048, "2 МБ/с"), new(5120, "5 МБ/с"), new(10240, "10 МБ/с"),
        new(20480, "20 МБ/с"), new(51200, "50 МБ/с")
    ];
    public DownloadLimitOption SelectedDownloadLimit
    {
        get => DownloadLimits.FirstOrDefault(x => x.KibPerSecond == Cfg.GetCVar(CVars.DownloadSpeedLimitKib)) ?? DownloadLimits[0];
        set
        {
            if (value == null) return;
            Cfg.SetCVar(CVars.DownloadSpeedLimitKib, value.KibPerSecond);
            Cfg.CommitConfig();
            OnPropertyChanged(nameof(SelectedDownloadLimit));
        }
    }

    public bool CustomUpdateChecks
    {
        get => Cfg.GetCVar(CVars.CustomUpdateChecks);
        set { Cfg.SetCVar(CVars.CustomUpdateChecks, value); Cfg.CommitConfig(); }
    }

    public void CheckLauncherUpdateNow() => _mainWindow?.CheckCustomLauncherUpdateManually();

    public void PreviewClickSound() => UiSoundService.Preview("click.wav");
    public void PreviewNavigationSound() => UiSoundService.Preview("navigation.wav");
    public void PreviewToggleSound() => UiSoundService.Preview("toggle.wav");
    public void PreviewNotificationSound() => UiSoundService.Preview("notification.wav");
    public void PreviewErrorSound() => UiSoundService.Preview("error.wav");

    public bool AutoRefreshFavoritePing
    {
        get => Cfg.GetCVar(CVars.AutoRefreshFavoritePing);
        set
        {
            Cfg.SetCVar(CVars.AutoRefreshFavoritePing, value);
            Cfg.CommitConfig();
        }
    }

    public bool FavoriteNotificationsEnabled
    {
        get => Cfg.GetCVar(CVars.FavoriteNotificationsEnabled);
        set => SetFavoriteNotification(CVars.FavoriteNotificationsEnabled, value);
    }

    public bool FavoriteNotifyServerOnline
    {
        get => Cfg.GetCVar(CVars.FavoriteNotifyServerOnline);
        set => SetFavoriteNotification(CVars.FavoriteNotifyServerOnline, value);
    }

    public bool FavoriteNotifyNewRound
    {
        get => Cfg.GetCVar(CVars.FavoriteNotifyNewRound);
        set => SetFavoriteNotification(CVars.FavoriteNotifyNewRound, value);
    }

    public bool FavoriteNotifySlotAvailable
    {
        get => Cfg.GetCVar(CVars.FavoriteNotifySlotAvailable);
        set => SetFavoriteNotification(CVars.FavoriteNotifySlotAvailable, value);
    }

    private void SetFavoriteNotification(CVarDef<bool> cvar, bool value)
    {
        Cfg.SetCVar(cvar, value);
        Cfg.CommitConfig();
    }

    public bool DiscordRpcEnabled { get => GetRpc(CVars.DiscordRpcEnabled); set => SetRpc(CVars.DiscordRpcEnabled, value); }
    public bool DiscordRpcShowNickname { get => GetRpc(CVars.DiscordRpcShowNickname); set => SetRpc(CVars.DiscordRpcShowNickname, value); }
    public bool DiscordRpcShowServer { get => GetRpc(CVars.DiscordRpcShowServer); set => SetRpc(CVars.DiscordRpcShowServer, value); }
    public bool DiscordRpcShowOnline { get => GetRpc(CVars.DiscordRpcShowOnline); set => SetRpc(CVars.DiscordRpcShowOnline, value); }
    public bool DiscordRpcShowPing { get => GetRpc(CVars.DiscordRpcShowPing); set => SetRpc(CVars.DiscordRpcShowPing, value); }
    public bool DiscordRpcShowMap { get => GetRpc(CVars.DiscordRpcShowMap); set => SetRpc(CVars.DiscordRpcShowMap, value); }
    public bool DiscordRpcShowGamePreset { get => GetRpc(CVars.DiscordRpcShowGamePreset); set => SetRpc(CVars.DiscordRpcShowGamePreset, value); }
    public bool DiscordRpcShowAvatar { get => GetRpc(CVars.DiscordRpcShowAvatar); set => SetRpc(CVars.DiscordRpcShowAvatar, value); }

    private bool GetRpc(CVarDef<bool> cvar) => Cfg.GetCVar(cvar);
    private void SetRpc(CVarDef<bool> cvar, bool value)
    {
        Cfg.SetCVar(cvar, value);
        Cfg.CommitConfig();
        DiscordRichPresenceService.Instance.RefreshSettings();
    }

    public override string Name => LocalizationManager.Instance.GetString("tab-options-title");
    public override string IconData => "M10,5 L3,5 M12,19 L3,19 M14,3 L14,7 M16,17 L16,21 M21,12 L12,12 M21,19 L16,19 M21,5 L14,5 M8,10 L8,14 M8,12 L3,12";

    public bool CompatMode
    {
        get => Cfg.GetCVar(CVars.CompatMode);
        set
        {
            Cfg.SetCVar(CVars.CompatMode, value);
            Cfg.CommitConfig();
        }
    }

    public bool LogLauncherVerbose
    {
        get => Cfg.GetCVar(CVars.LogLauncherVerbose);
        set
        {
            Cfg.SetCVar(CVars.LogLauncherVerbose, value);
            Cfg.CommitConfig();
        }
    }

    public void ClearEngines()
    {
        _engineManager.ClearAllEngines();
    }

    public async Task<bool> ClearServerContent()
    {
        return await _contentManager.ClearAll();
    }

    public void OpenLogDirectory()
    {
        Process.Start(new ProcessStartInfo
        {
            UseShellExecute = true,
            FileName = LauncherPaths.DirLogs
        });
    }

    public void OpenAccountSettings()
    {
        Helpers.OpenUri(ConfigConstants.AccountManagementUrl);
    }

    public void OpenLastCrashReport()
    {
        var path = Path.Combine(LauncherPaths.DirLogs, "last-crash.txt");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = path });
        else _mainWindow?.ShowToast("Отчёт о последнем сбое отсутствует");
    }

    public async void ExportSettingsBackup()
    {
        if (_mainWindow?.Control?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Резервная копия настроек", SuggestedFileName = "ss14-launcher-settings.zip", DefaultExtension = "zip" });
        var path = file?.TryGetLocalPath(); if (path == null) return;
        try { SettingsBackupService.Export(path, Cfg); _mainWindow.ShowToast("Резервная копия создана"); }
        catch { _mainWindow.ShowToast("Не удалось создать резервную копию", true); }
    }

    public async void ImportSettingsBackup()
    {
        if (_mainWindow?.Control?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Восстановление настроек", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Архив настроек") { Patterns = ["*.zip"] }] });
        var path = files.FirstOrDefault()?.TryGetLocalPath(); if (path == null) return;
        try { SettingsBackupService.Import(path, Cfg); App.ApplyConfiguredTheme(Cfg); _mainWindow.ShowToast("Настройки восстановлены. Перезапустите лаунчер"); }
        catch { _mainWindow.ShowToast("Архив настроек повреждён", true); }
    }
}

public sealed record DownloadLimitOption(int KibPerSecond, string Name)
{
    public override string ToString() => Name;
}

public sealed class NavigationTabOptionViewModel : ObservableObject
{
    private readonly OptionsTabViewModel _owner;
    private readonly MainWindowViewModel _mainWindow;
    private readonly string _id;
    public string Name { get; }
    public bool CanHide => _mainWindow.CanHideNavigationTab(_id);
    public bool IsVisible
    {
        get => _mainWindow.IsNavigationTabVisible(_id);
        set
        {
            if (value == IsVisible) return;
            _mainWindow.SetNavigationTabVisible(_id, value);
            _owner.InitializeNavigation();
        }
    }

    private int Index => _mainWindow.AllTabs.ToList().FindIndex(t => _mainWindow.GetNavigationId(t) == _id);
    public bool CanMoveUp => Index > 0;
    public bool CanMoveDown => Index >= 0 && Index < _mainWindow.AllTabs.Count - 1;

    public NavigationTabOptionViewModel(OptionsTabViewModel owner, MainWindowViewModel mainWindow, MainWindowTabViewModel tab)
    {
        _owner = owner;
        _mainWindow = mainWindow;
        _id = mainWindow.GetNavigationId(tab);
        Name = tab.Name;
    }

    public void MoveUp() { _mainWindow.MoveNavigationTab(_id, -1); _owner.InitializeNavigation(); }
    public void MoveDown() { _mainWindow.MoveNavigationTab(_id, 1); _owner.InitializeNavigation(); }
}
