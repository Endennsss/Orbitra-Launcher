using System;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using SS14.Launcher.Utility;

namespace SS14.Launcher.Models.Data;

/// <summary>
/// Contains definitions for all launcher configuration values.
/// </summary>
/// <remarks>
/// The fields of this class are automatically searched for all CVar definitions.
/// </remarks>
/// <see cref="DataManager"/>
[UsedImplicitly]
public static class CVars
{
    /// <summary>
    /// Default to using compatibility options for rendering etc,
    /// that are less likely to immediately crash on buggy drivers.
    /// </summary>
    public static readonly CVarDef<bool> CompatMode = CVarDef.Create("CompatMode", false);

    /// <summary>
    /// On first launch, the launcher tells you that SS14 is EARLY ACCESS.
    /// This stores whether they dismissed that, though people will insist on pretending it defaults to true.
    /// </summary>
    public static readonly CVarDef<bool> HasDismissedEarlyAccessWarning
        = CVarDef.Create("HasDismissedEarlyAccessWarning", false);

    /// <summary>
    /// Used to warn users about the degradation of the Intel 13th and 14th generation CPUs
    /// This has proven multiple times to cause issues with game startup due to some memory access issue after enough degradation.
    /// <see href="https://www.reddit.com/r/intel/comments/1egthzw/megathread_for_intel_core_13th_14th_gen_cpu/"/>
    /// </summary>
    public static readonly CVarDef<bool> HasDismissedIntelDegradation
        = CVarDef.Create("HasDismissedIntelDegradation", false);

    /// <summary>
    /// Used to warn Apple Silicon users who are running the game under Rosetta 2 when they could be running the native build.
    /// </summary>
    public static readonly CVarDef<bool> HasDismissedRosettaWarning
        = CVarDef.Create("HasDismissedRosettaWarning", false);

    /// <summary>
    /// Disable checking engine build signatures when launching game.
    /// Only enable if you know what you're doing.
    /// </summary>
    /// <remarks>
    /// This is ignored on release builds, for security reasons.
    /// </remarks>
    public static readonly CVarDef<bool> DisableSigning = CVarDef.Create("DisableSigning", false);

    /// <summary>
    /// Enable local overriding of engine versions.
    /// </summary>
    /// <remarks>
    /// If enabled and on a development build,
    /// the launcher will pull all engine versions and modules from <see cref="EngineOverridePath"/>.
    /// This can be set to <c>RobustToolbox/release/</c> to instantly pull in packaged engine builds.
    /// </remarks>
    public static readonly CVarDef<bool> EngineOverrideEnabled = CVarDef.Create("EngineOverrideEnabled", false);

    /// <summary>
    /// Path to load engines from when using <see cref="EngineOverrideEnabled"/>.
    /// </summary>
    public static readonly CVarDef<string> EngineOverridePath = CVarDef.Create("EngineOverridePath", "");

    /// <summary>
    /// Verbose logging of launcher logs.
    /// </summary>
    public static readonly CVarDef<bool> LogLauncherVerbose = CVarDef.Create("LogLauncherVerbose", false);

    /// <summary>
    /// Enable multi-account support on release builds.
    /// </summary>
    public static readonly CVarDef<bool> MultiAccounts = CVarDef.Create("MultiAccounts", false);

    /// <summary>
    /// Currently selected login in the drop down.
    /// </summary>
    public static readonly CVarDef<string> SelectedLogin = CVarDef.Create("SelectedLogin", "");

    public static readonly CVarDef<string> Fingerprint = CVarDef.Create("Fingerprint", "");

    /// <summary>
    /// Maximum amount of TOTAL versions to keep in the content database.
    /// </summary>
    public static readonly CVarDef<int> MaxVersionsToKeep = CVarDef.Create("MaxVersionsToKeep", 15);

    /// <summary>
    /// Maximum amount of versions to keep of a specific fork ID.
    /// </summary>
    public static readonly CVarDef<int> MaxForkVersionsToKeep = CVarDef.Create("MaxForkVersionsToKeep", 3);

     /// <summary>
    /// If a download gets interrupted, keep the files for a week.
    /// </summary>
    public static readonly CVarDef<int> InterruptibleDownloadKeepHours = CVarDef.Create("InterruptibleDownloadKeepHours", 7 * 24);

    /// <summary>
    /// Whether to display override assets (trans rights).
    /// </summary>
    public static readonly CVarDef<bool> OverrideAssets = CVarDef.Create("OverrideAssets", true);

    /// <summary>
    /// Stores the minimum player count value used by the "minimum player count" filter.
    /// </summary>
    /// <seealso cref="ServerFilter.PlayerCountMin"/>
    public static readonly CVarDef<int> FilterPlayerCountMinValue = CVarDef.Create("FilterPlayerCountMinValue", 0);

    /// <summary>
    /// Stores the maximum player count value used by the "maximum player count" filter.
    /// </summary>
    /// <seealso cref="ServerFilter.PlayerCountMax"/>
    public static readonly CVarDef<int> FilterPlayerCountMaxValue = CVarDef.Create("FilterPlayerCountMaxValue", 0);

    /// <summary>
    /// Stores whether the user has seen the Wine warning.
    /// </summary>
    public static readonly CVarDef<bool> WineWarningShown = CVarDef.Create("WineWarningShown", false);

    /// <summary>
    /// Language the user selected. Null means it should be automatically selected based on system language.
    /// </summary>
    public static readonly CVarDef<string?> Language = CVarDef.Create<string?>("Language", null);

    public static readonly CVarDef<bool> UseTextLogo = CVarDef.Create("UseTextLogo", false);

    public static readonly CVarDef<string> LauncherFont = CVarDef.Create("LauncherFont", "Noto Sans");

    /// <summary>
    /// Use the neutral light launcher palette instead of the default dark palette.
    /// </summary>
    public static readonly CVarDef<bool> LightTheme = CVarDef.Create("LightTheme", false);
    public static readonly CVarDef<bool> CustomThemeEnabled = CVarDef.Create("CustomThemeEnabled", false);
    public static readonly CVarDef<string> CustomThemeBackground = CVarDef.Create("CustomThemeBackground", "#101010");
    public static readonly CVarDef<string> CustomThemeSurface = CVarDef.Create("CustomThemeSurface", "#181818");
    public static readonly CVarDef<string> CustomThemeControl = CVarDef.Create("CustomThemeControl", "#292929");
    public static readonly CVarDef<string> CustomThemeAccent = CVarDef.Create("CustomThemeAccent", "#D0D0D0");
    public static readonly CVarDef<string> CustomThemeText = CVarDef.Create("CustomThemeText", "#F2F2F2");
    public static readonly CVarDef<string> CustomThemeMuted = CVarDef.Create("CustomThemeMuted", "#999999");
    public static readonly CVarDef<string> CustomThemeImage = CVarDef.Create("CustomThemeImage", "");
    public static readonly CVarDef<string> CustomThemeImageHome = CVarDef.Create("CustomThemeImageHome", "");
    public static readonly CVarDef<string> CustomThemeImageServers = CVarDef.Create("CustomThemeImageServers", "");
    public static readonly CVarDef<string> CustomThemeImageOptions = CVarDef.Create("CustomThemeImageOptions", "");
    public static readonly CVarDef<int> CustomThemeBlur = CVarDef.Create("CustomThemeBlur", 8);
    public static readonly CVarDef<int> CustomThemeDimming = CVarDef.Create("CustomThemeDimming", 28);
    public static readonly CVarDef<bool> UiSoundsEnabled = CVarDef.Create("UiSoundsEnabled", true);
    public static readonly CVarDef<int> UiSoundVolume = CVarDef.Create("UiSoundVolume", 65);
    public static readonly CVarDef<bool> CloseToTray = CVarDef.Create("CloseToTray", true);
    public static readonly CVarDef<string> NavigationTabOrder =
        CVarDef.Create("NavigationTabOrder", "home,servers,news,links,playtime,activity,system-center,custom-theme,options,development");
    public static readonly CVarDef<string> HiddenNavigationTabs =
        CVarDef.Create("HiddenNavigationTabs", "");
    public static readonly CVarDef<int> NavigationOrderVersion = CVarDef.Create("NavigationOrderVersion", 0);

    /// <summary>
    /// Automatically refresh favorite server status and ping every three seconds.
    /// </summary>
    public static readonly CVarDef<bool> AutoRefreshFavoritePing =
        CVarDef.Create("AutoRefreshFavoritePing", true);
    public static readonly CVarDef<bool> FavoriteNotificationsEnabled =
        CVarDef.Create("FavoriteNotificationsEnabled", true);
    public static readonly CVarDef<string> MonitoredFavoriteServers =
        CVarDef.Create("MonitoredFavoriteServers", "");
    public static readonly CVarDef<bool> FavoriteNotifyServerOnline =
        CVarDef.Create("FavoriteNotifyServerOnline", true);
    public static readonly CVarDef<bool> FavoriteNotifyNewRound =
        CVarDef.Create("FavoriteNotifyNewRound", true);
    public static readonly CVarDef<bool> FavoriteNotifySlotAvailable =
        CVarDef.Create("FavoriteNotifySlotAvailable", true);
    public static readonly CVarDef<bool> DiscordRpcEnabled = CVarDef.Create("DiscordRpcEnabled", true);
    public static readonly CVarDef<bool> DiscordRpcShowNickname = CVarDef.Create("DiscordRpcShowNickname", true);
    public static readonly CVarDef<bool> DiscordRpcShowServer = CVarDef.Create("DiscordRpcShowServer", true);
    public static readonly CVarDef<bool> DiscordRpcShowOnline = CVarDef.Create("DiscordRpcShowOnline", true);
    public static readonly CVarDef<bool> DiscordRpcShowPing = CVarDef.Create("DiscordRpcShowPing", true);
    public static readonly CVarDef<bool> DiscordRpcShowMap = CVarDef.Create("DiscordRpcShowMap", true);
    public static readonly CVarDef<bool> DiscordRpcShowGamePreset = CVarDef.Create("DiscordRpcShowGamePreset", true);
    public static readonly CVarDef<bool> FirstRunCompleted = CVarDef.Create("FirstRunCompleted", false);
    public static readonly CVarDef<string> LastReadLauncherNews = CVarDef.Create("LastReadLauncherNews", "");
    public static readonly CVarDef<bool> CustomUpdateChecks = CVarDef.Create("CustomUpdateChecks", true);

    /// <summary>
    /// The CPU architecture this launcher was last run with.
    /// </summary>
    /// <remarks>
    /// Used to delete engine builds of other architectures on startup.
    /// Defaults to x64 so that people upgrading to a proper ARM64 launcher on e.g. Apple Silicon
    /// properly get their existing installations cleared.
    /// </remarks>
    public static readonly CVarDef<int> CurrentArchitecture = CVarDef.Create("CurrentArchitecture", (int) Architecture.X64);
}

/// <summary>
/// Base definition of a CVar.
/// </summary>
/// <seealso cref="DataManager"/>
/// <seealso cref="CVars"/>
public abstract class CVarDef
{
    public string Name { get; }
    public object? DefaultValue { get; }
    public Type ValueType { get; }

    private protected CVarDef(string name, object? defaultValue, Type type)
    {
        Name = name;
        DefaultValue = defaultValue;
        ValueType = type;
    }

    public static CVarDef<T> Create<T>(
        string name,
        T defaultValue)
    {
        return new CVarDef<T>(name, defaultValue);
    }
}

/// <summary>
/// Generic specialized definition of CVar definition.
/// </summary>
/// <typeparam name="T">The type of value stored in this CVar.</typeparam>
public sealed class CVarDef<T> : CVarDef
{
    public new T DefaultValue { get; }

    internal CVarDef(string name, T defaultValue) : base(name, defaultValue, typeof(T))
    {
        DefaultValue = defaultValue;
    }
}
