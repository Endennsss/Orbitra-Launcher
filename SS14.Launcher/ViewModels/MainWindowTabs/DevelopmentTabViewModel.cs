using System;
using Splat;
using SS14.Launcher.Localization;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class DevelopmentTabViewModel : MainWindowTabViewModel
{
    private readonly LocalizationManager _loc = LocalizationManager.Instance;
    private readonly DataManager _cfg = Locator.Current.GetRequiredService<DataManager>();
    private readonly MainWindowViewModel? _mainWindow;
    private double _testProgress = 0.35;
    private bool _progressIndeterminate;

    public DevelopmentTabViewModel() : this(null)
    {
    }

    public DevelopmentTabViewModel(MainWindowViewModel? mainWindow)
    {
        _mainWindow = mainWindow;
        _cfg.GetCVarEntry(CVars.EngineOverrideEnabled).PropertyChanged += (_, _) =>
            OnPropertyChanged(nameof(Name));
    }

    public override string Name => _cfg.GetCVar(CVars.EngineOverrideEnabled)
        ? _loc.GetString("tab-development-title-override")
        : _loc.GetString("tab-development-title");

    public override string IconData => "M8,9 L3,12 L8,15 M16,9 L21,12 L16,15 M14,5 L10,19";

    public double TestProgress
    {
        get => _testProgress;
        private set => SetProperty(ref _testProgress, value);
    }

    public bool ProgressIndeterminate
    {
        get => _progressIndeterminate;
        private set => SetProperty(ref _progressIndeterminate, value);
    }

    public void ShowInfoToast() => _mainWindow?.ShowToast("Тестовое информационное сообщение");
    public void ShowErrorToast() => _mainWindow?.ShowToast("Тестовая ошибка интерфейса", true);
    public void NotifyServerOnline() => SystemNotificationService.Show("Сервер снова доступен", "DEV Test Server");
    public void NotifyNewRound() => SystemNotificationService.Show("Начался новый раунд", "DEV Test Server · Bagel · Secret");
    public void NotifySlotAvailable() => SystemNotificationService.Show("На сервере появилось место", "DEV Test Server · 49/50");
    public void RpcLauncher() => DiscordRichPresenceService.Instance.ShowLauncher();
    public void RpcSearching() => DiscordRichPresenceService.Instance.ShowSearching();

    public void RpcSelected() => DiscordRichPresenceService.Instance.SelectServer(
        "DEV Test Server", "ss14://localhost:1212", 42, 50,
        TimeSpan.FromMilliseconds(67), "Bagel", "Secret");

    public void RpcPlaying()
    {
        RpcSelected();
        DiscordRichPresenceService.Instance.ShowPlaying();
    }

    public void PreviewDarkTheme() => App.ApplyColorTheme(false);
    public void PreviewLightTheme() => App.ApplyColorTheme(true);

    public void AdvanceProgress()
    {
        ProgressIndeterminate = false;
        TestProgress = TestProgress >= 1 ? 0 : Math.Min(1, TestProgress + 0.15);
    }

    public void ToggleIndeterminate() => ProgressIndeterminate = !ProgressIndeterminate;

    public void ResetProgress()
    {
        ProgressIndeterminate = false;
        TestProgress = 0;
    }

    public bool DisableSigning
    {
        get => _cfg.GetCVar(CVars.DisableSigning);
        set
        {
            _cfg.SetCVar(CVars.DisableSigning, value);
            _cfg.CommitConfig();
        }
    }

    public bool EngineOverrideEnabled
    {
        get => _cfg.GetCVar(CVars.EngineOverrideEnabled);
        set
        {
            _cfg.SetCVar(CVars.EngineOverrideEnabled, value);
            _cfg.CommitConfig();
        }
    }

    public string EngineOverridePath
    {
        get => _cfg.GetCVar(CVars.EngineOverridePath);
        set
        {
            _cfg.SetCVar(CVars.EngineOverridePath, value);
            _cfg.CommitConfig();
        }
    }
}
