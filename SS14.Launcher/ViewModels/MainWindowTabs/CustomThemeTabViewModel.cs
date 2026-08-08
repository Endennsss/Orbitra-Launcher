using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AnimatedImage.Avalonia;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;
using SS14.Launcher.Views;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class CustomThemeTabViewModel : MainWindowTabViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly DataManager _cfg;
    private readonly ThemeWorkshopService _workshop = new();
    private Bitmap? _backgroundBitmap;
    private AnimatedImageSource? _backgroundAnimation;
    private readonly Dictionary<string, Bitmap?> _tabBackgrounds = new(StringComparer.OrdinalIgnoreCase);
    private ThemePackage? _pendingTheme;
    private Bitmap? _pendingBackgroundBitmap;
    private double _themeTransitionOpacity;
    private CancellationTokenSource? _transitionCancellation;
    public ObservableCollection<ThemeBackgroundOptionViewModel> TabBackgroundOptions { get; } = [];
    public ObservableCollection<ThemeLibraryItemViewModel> ThemeLibrary { get; } = [];
    public ObservableCollection<WorkshopThemeItemViewModel> WorkshopThemes { get; } = [];
    public ObservableCollection<WorkshopCommentItemViewModel> WorkshopComments { get; } = [];
    private readonly List<WorkshopThemeDto> _allWorkshopThemes = [];
    private WorkshopThemeItemViewModel? _selectedWorkshopTheme;
    private bool _workshopBusy;
    private string _workshopStatus = "Загрузка мастерской…";
    private string _publishName = "";
    private string _publishDescription = "";
    private WorkshopThemeItemViewModel? _editingWorkshopTheme;
    private string _newComment = "";
    private string _workshopSearch = "";
    private int _workshopSortIndex;
    private bool _workshopFavoritesOnly;
    private byte[]? _previewRestoreArchive;
    private string _previewRestoreInstalledId = "";
    private WorkshopThemeItemViewModel? _previewWorkshopTheme;
    private WorkshopThemeItemViewModel? _pendingDeleteWorkshopTheme;
    private ThemeWorkshopWindow? _workshopWindow;
    private ThemePublishWindow? _publishWindow;
    private Bitmap? _publishIcon;
    private Bitmap? _publishScreenshot;
    private byte[]? _publishScreenshotBytes;
    public ObservableCollection<PublishScreenshotItem> PublishScreenshots { get; } = [];

    public CustomThemeTabViewModel(MainWindowViewModel main)
    {
        _main = main;
        _cfg = main.Cfg;
        LoadBackground();
        foreach (var (key, name) in new[] { ("home", "Главная"), ("servers", "Серверы"), ("options", "Настройки") })
            TabBackgroundOptions.Add(new ThemeBackgroundOptionViewModel(this, key, name));
        LoadThemeLibrary();
        _ = RefreshWorkshopAsync();
    }

    public override string Name => "Кастом тема";
    // Lucide "palette" icon (MIT), converted from its official SVG into Avalonia path data.
    public override string IconData => "M12,22 A10,10 0 1 1 22,12 A4,4 0 0 1 18,16 L15.5,16 A2,2 0 0 0 14,19.3 L14.2,19.6 A1.5,1.5 0 0 1 12.8,22 Z M7.5,10 L7.51,10 M9.5,6.5 L9.51,6.5 M14.5,6.5 L14.51,6.5 M17,10 L17.01,10";

    public bool Enabled
    {
        get => _cfg.GetCVar(CVars.CustomThemeEnabled);
        set
        {
            ClearInstalledWorkshopTheme();
            _cfg.SetCVar(CVars.CustomThemeEnabled, value);
            if (value) _cfg.SetCVar(CVars.LightTheme, false);
            SaveAndApplyAnimated();
            _main.OptionsTab.RefreshThemeAvailability();
            OnPropertyChanged(nameof(BackgroundVisible));
            _main.RefreshThemeVisuals();
        }
    }

    public string Background { get => _cfg.GetCVar(CVars.CustomThemeBackground); set => SetColor(CVars.CustomThemeBackground, value, nameof(Background)); }
    public string Surface { get => _cfg.GetCVar(CVars.CustomThemeSurface); set => SetColor(CVars.CustomThemeSurface, value, nameof(Surface)); }
    public string Control { get => _cfg.GetCVar(CVars.CustomThemeControl); set => SetColor(CVars.CustomThemeControl, value, nameof(Control)); }
    public string Accent { get => _cfg.GetCVar(CVars.CustomThemeAccent); set => SetColor(CVars.CustomThemeAccent, value, nameof(Accent)); }
    public string Text { get => _cfg.GetCVar(CVars.CustomThemeText); set => SetColor(CVars.CustomThemeText, value, nameof(Text)); }
    public string Muted { get => _cfg.GetCVar(CVars.CustomThemeMuted); set => SetColor(CVars.CustomThemeMuted, value, nameof(Muted)); }
    public Color BackgroundColor { get => ParseColor(Background); set => Background = value.ToString(); }
    public Color SurfaceColor { get => ParseColor(Surface); set => Surface = value.ToString(); }
    public Color ControlColor { get => ParseColor(Control); set => Control = value.ToString(); }
    public Color AccentColor { get => ParseColor(Accent); set => Accent = value.ToString(); }
    public Color TextColor { get => ParseColor(Text); set => Text = value.ToString(); }
    public Color MutedColor { get => ParseColor(Muted); set => Muted = value.ToString(); }

    public int Blur
    {
        get => _cfg.GetCVar(CVars.CustomThemeBlur);
        set
        {
            var blur = Math.Clamp(value, 0, 40);
            if (blur == Blur) return;
            ClearInstalledWorkshopTheme();
            _cfg.SetCVar(CVars.CustomThemeBlur, blur);
            _cfg.CommitConfig();
            OnPropertyChanged(nameof(Blur));
        }
    }

    public int Dimming
    {
        get => _cfg.GetCVar(CVars.CustomThemeDimming);
        set
        {
            var dimming = Math.Clamp(value, 0, 90);
            if (dimming == Dimming) return;
            ClearInstalledWorkshopTheme();
            _cfg.SetCVar(CVars.CustomThemeDimming, dimming);
            _cfg.CommitConfig();
            OnPropertyChanged(nameof(Dimming));
            OnPropertyChanged(nameof(BackgroundDimmingOpacity));
        }
    }

    public double BackgroundDimmingOpacity => Dimming / 100d;

    public Bitmap? BackgroundBitmap => _backgroundBitmap;
    public AnimatedImageSource? BackgroundAnimation => _backgroundAnimation;
    public bool BackgroundIsAnimated => _backgroundAnimation != null;
    public bool BackgroundVisible => Enabled && File.Exists(_cfg.GetCVar(CVars.CustomThemeImage));
    public double ThemeTransitionOpacity
    {
        get => _themeTransitionOpacity;
        private set { _themeTransitionOpacity = value; OnPropertyChanged(nameof(ThemeTransitionOpacity)); }
    }
    public bool HasImportPreview => _pendingTheme != null;
    public Bitmap? ImportPreviewBackground => _pendingBackgroundBitmap;
    public string ImportPreviewColors => _pendingTheme == null ? "" :
        $"{_pendingTheme.Manifest.Background}  {_pendingTheme.Manifest.Surface}  {_pendingTheme.Manifest.Accent}  {_pendingTheme.Manifest.Text}";
    public int ImportPreviewBlur => _pendingTheme?.Manifest.Blur ?? 0;
    public double ImportPreviewDimmingOpacity => (_pendingTheme?.Manifest.Dimming ?? 28) / 100d;
    public bool HasContrastWarning => CalculateContrast(ParseColor(Text), ParseColor(Background)) < 4.5;
    public string ContrastMessage => HasContrastWarning
        ? $"Контраст текста и фона: {CalculateContrast(ParseColor(Text), ParseColor(Background)):0.00}:1. Рекомендуется минимум 4.5:1."
        : $"Контраст текста и фона хороший: {CalculateContrast(ParseColor(Text), ParseColor(Background)):0.00}:1.";

    public Bitmap? GetBackgroundFor(string tabId)
    {
        if (!Enabled) return null;
        var path = GetBackgroundPathFor(tabId);
        if (IsAnimatedImage(path)) return null;
        return _backgroundBitmap;
    }

    public string? GetBackgroundPathFor(string tabId)
    {
        if (!Enabled) return null;
        var commonPath = _cfg.GetCVar(CVars.CustomThemeImage);
        return File.Exists(commonPath) ? commonPath : null;
    }

    public static bool IsAnimatedImage(string? path) =>
        Path.GetExtension(path ?? "").Equals(".gif", StringComparison.OrdinalIgnoreCase);

    public async void ChooseBackground()
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите фон темы",
            AllowMultiple = false,
            FileTypeFilter = [ThemeImageFileType]
        });
        var source = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            var extension = Path.GetExtension(source);
            var destination = Path.Combine(LauncherPaths.DirUserData, $"custom-theme-background{extension}");
            Directory.CreateDirectory(LauncherPaths.DirUserData);
            File.Copy(source, destination, true);
            ClearInstalledWorkshopTheme();
            _cfg.SetCVar(CVars.CustomThemeImage, destination);
            _cfg.CommitConfig();
            LoadBackground();
        }
        catch { _main.ShowToast("Не удалось загрузить изображение", true); }
    }

    public void RemoveBackground()
    {
        ClearInstalledWorkshopTheme();
        _cfg.SetCVar(CVars.CustomThemeImage, "");
        _cfg.SetCVar(CVars.CustomThemeImageHome, "");
        _cfg.SetCVar(CVars.CustomThemeImageServers, "");
        _cfg.SetCVar(CVars.CustomThemeImageOptions, "");
        _cfg.CommitConfig();
        _backgroundBitmap?.Dispose();
        _backgroundBitmap = null;
        _backgroundAnimation = null;
        LoadTabBackgrounds();
        OnPropertyChanged(nameof(BackgroundBitmap));
        OnPropertyChanged(nameof(BackgroundAnimation));
        OnPropertyChanged(nameof(BackgroundIsAnimated));
        OnPropertyChanged(nameof(BackgroundVisible));
        _main.RefreshThemeVisuals();
    }

    public void ResetDefaults()
    {
        ClearInstalledWorkshopTheme();
        _cfg.SetCVar(CVars.CustomThemeBackground, "#101010");
        _cfg.SetCVar(CVars.CustomThemeSurface, "#181818");
        _cfg.SetCVar(CVars.CustomThemeControl, "#292929");
        _cfg.SetCVar(CVars.CustomThemeAccent, "#D0D0D0");
        _cfg.SetCVar(CVars.CustomThemeText, "#F2F2F2");
        _cfg.SetCVar(CVars.CustomThemeMuted, "#999999");
        _cfg.SetCVar(CVars.CustomThemeBlur, 8);
        _cfg.SetCVar(CVars.CustomThemeDimming, 28);
        _cfg.SetCVar(CVars.CustomThemeImage, "");
        _cfg.CommitConfig();
        _backgroundBitmap?.Dispose();
        _backgroundBitmap = null;
        foreach (var property in new[] { nameof(Background), nameof(Surface), nameof(Control), nameof(Accent), nameof(Text), nameof(Muted) })
        {
            OnPropertyChanged(property);
            OnPropertyChanged(property + "Color");
        }
        OnPropertyChanged(nameof(Blur));
        OnPropertyChanged(nameof(Dimming));
        OnPropertyChanged(nameof(BackgroundDimmingOpacity));
        OnPropertyChanged(nameof(BackgroundBitmap));
        OnPropertyChanged(nameof(BackgroundVisible));
        if (Enabled) BeginThemeTransition();
        _main.RefreshThemeVisuals();
        _main.ShowToast("Настройки кастомной темы сброшены");
    }

    public async void ExportTheme()
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт кастомной темы",
            SuggestedFileName = "launcher-theme.zip",
            DefaultExtension = "zip",
            FileTypeChoices = [new FilePickerFileType("Архив темы") { Patterns = ["*.zip"] }]
        });
        var outputPath = file?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(outputPath)) return;
        try
        {
            WriteThemeArchive(outputPath);
            _main.ShowToast("Тема экспортирована");
        }
        catch { _main.ShowToast("Не удалось экспортировать тему", true); }
    }

    public async void ImportTheme()
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Импорт кастомной темы",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Архив темы") { Patterns = ["*.zip"] }]
        });
        var inputPath = files.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(inputPath)) return;
        try { SetImportPreview(ReadThemeArchive(inputPath)); }
        catch { _main.ShowToast("Архив темы повреждён или имеет неподдерживаемый формат", true); }
    }

    public void ApplyImportPreview()
    {
        if (_pendingTheme == null) return;
        ApplyPackage(_pendingTheme);
        CancelImportPreview();
        _main.ShowToast("Тема импортирована");
    }

    public void CancelImportPreview()
    {
        _pendingTheme = null;
        _pendingBackgroundBitmap?.Dispose();
        _pendingBackgroundBitmap = null;
        OnPropertyChanged(nameof(HasImportPreview));
        OnPropertyChanged(nameof(ImportPreviewBackground));
        OnPropertyChanged(nameof(ImportPreviewColors));
        OnPropertyChanged(nameof(ImportPreviewBlur));
        OnPropertyChanged(nameof(ImportPreviewDimmingOpacity));
    }

    private void RefreshAllProperties()
    {
        OnPropertyChanged(nameof(Enabled));
        foreach (var property in new[] { nameof(Background), nameof(Surface), nameof(Control), nameof(Accent), nameof(Text), nameof(Muted) })
        {
            OnPropertyChanged(property);
            OnPropertyChanged(property + "Color");
        }
        OnPropertyChanged(nameof(Blur));
        OnPropertyChanged(nameof(Dimming));
        OnPropertyChanged(nameof(BackgroundDimmingOpacity));
        OnPropertyChanged(nameof(BackgroundVisible));
    }

    private void WriteThemeArchive(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        string? AddImage(string source, string name)
        {
            if (!File.Exists(source)) return null;
            var entryName = name + Path.GetExtension(source).ToLowerInvariant();
            archive.CreateEntryFromFile(source, entryName, CompressionLevel.Optimal);
            return entryName;
        }
        var imageName = AddImage(_cfg.GetCVar(CVars.CustomThemeImage), "background");
        var tabs = new Dictionary<string, string>();
        foreach (var key in new[] { "home", "servers", "options" })
        {
            var entry = AddImage(_cfg.GetCVar(GetTabBackgroundCVar(key)), "background-" + key);
            if (entry != null) tabs[key] = entry;
        }
        var manifest = new ThemeArchive(1, Enabled, Background, Surface, Control, Accent, Text, Muted, Blur, imageName, tabs, Dimming);
        var manifestEntry = archive.CreateEntry("theme.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(manifestEntry.Open());
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ThemePackage ReadThemeArchive(string path)
    {
        var archiveBytes = File.ReadAllBytes(path);
        ThemeArchiveValidator.Validate(archiveBytes);
        using var archive = new ZipArchive(new MemoryStream(archiveBytes, writable: false), ZipArchiveMode.Read);
        var manifestEntry = archive.GetEntry("theme.json") ?? throw new InvalidDataException("theme.json missing");
        if (manifestEntry.Length > 64 * 1024) throw new InvalidDataException("Manifest too large");
        ThemeArchive manifest;
        using (var reader = new StreamReader(manifestEntry.Open()))
            manifest = JsonSerializer.Deserialize<ThemeArchive>(reader.ReadToEnd()) ?? throw new InvalidDataException();
        if (manifest.Version != 1) throw new InvalidDataException("Unsupported version");
        foreach (var color in new[] { manifest.Background, manifest.Surface, manifest.Control, manifest.Accent, manifest.Text, manifest.Muted })
            Color.Parse(color);
        var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        if (manifest.BackgroundFile != null) names.Add(manifest.BackgroundFile);
        if (manifest.TabBackgroundFiles != null) names.AddRange(manifest.TabBackgroundFiles.Values);
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)) throw new InvalidDataException();
            var extension = Path.GetExtension(name).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif")) throw new InvalidDataException();
            var entry = archive.GetEntry(name) ?? throw new InvalidDataException();
            if (entry.Length > 25L * 1024 * 1024) throw new InvalidDataException();
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            images[name] = memory.ToArray();
        }
        var allowedEntries = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase) { "theme.json" };
        if (archive.Entries.Any(entry => !allowedEntries.Contains(entry.FullName)))
            throw new InvalidDataException("Небезопасная тема: архив содержит файлы, не указанные в theme.json.");
        return new ThemePackage(manifest, images);
    }

    private void SetImportPreview(ThemePackage package)
    {
        CancelImportPreview();
        _pendingTheme = package;
        if (package.Manifest.BackgroundFile is { } name && package.Images.TryGetValue(name, out var bytes))
            _pendingBackgroundBitmap = new Bitmap(new MemoryStream(bytes));
        OnPropertyChanged(nameof(HasImportPreview));
        OnPropertyChanged(nameof(ImportPreviewBackground));
        OnPropertyChanged(nameof(ImportPreviewColors));
        OnPropertyChanged(nameof(ImportPreviewBlur));
        OnPropertyChanged(nameof(ImportPreviewDimmingOpacity));
    }

    private void ApplyPackage(ThemePackage package)
    {
        ClearInstalledWorkshopTheme();
        var manifest = package.Manifest;
        string SaveImage(string? entryName, string targetName)
        {
            if (entryName == null || !package.Images.TryGetValue(entryName, out var bytes)) return "";
            var path = Path.Combine(LauncherPaths.DirUserData, targetName + Path.GetExtension(entryName).ToLowerInvariant());
            File.WriteAllBytes(path, bytes);
            return path;
        }
        _cfg.SetCVar(CVars.CustomThemeEnabled, manifest.Enabled);
        if (manifest.Enabled) _cfg.SetCVar(CVars.LightTheme, false);
        _cfg.SetCVar(CVars.CustomThemeBackground, manifest.Background);
        _cfg.SetCVar(CVars.CustomThemeSurface, manifest.Surface);
        _cfg.SetCVar(CVars.CustomThemeControl, manifest.Control);
        _cfg.SetCVar(CVars.CustomThemeAccent, manifest.Accent);
        _cfg.SetCVar(CVars.CustomThemeText, manifest.Text);
        _cfg.SetCVar(CVars.CustomThemeMuted, manifest.Muted);
        _cfg.SetCVar(CVars.CustomThemeBlur, Math.Clamp(manifest.Blur, 0, 40));
        _cfg.SetCVar(CVars.CustomThemeDimming, Math.Clamp(manifest.Dimming, 0, 90));
        _cfg.SetCVar(CVars.CustomThemeImage, SaveImage(manifest.BackgroundFile, "custom-theme-background"));
        foreach (var key in new[] { "home", "servers", "options" })
        {
            string? entryName = null;
            manifest.TabBackgroundFiles?.TryGetValue(key, out entryName);
            _cfg.SetCVar(GetTabBackgroundCVar(key), SaveImage(entryName, "custom-theme-" + key));
        }
        _cfg.CommitConfig();
        RefreshAllProperties();
        LoadBackground();
        BeginThemeTransition();
        _main.OptionsTab.RefreshThemeAvailability();
    }

    private sealed record ThemeArchive(int Version, bool Enabled, string Background, string Surface,
        string Control, string Accent, string Text, string Muted, int Blur, string? BackgroundFile,
        Dictionary<string, string>? TabBackgroundFiles = null, int Dimming = 28);
    private sealed record ThemePackage(ThemeArchive Manifest, Dictionary<string, byte[]> Images);

    private void SetColor(CVarDef<string> cvar, string value, string property)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { Color.Parse(value.Trim()); }
        catch { return; }
        ClearInstalledWorkshopTheme();
        _cfg.SetCVar(cvar, value.Trim());
        _cfg.CommitConfig();
        OnPropertyChanged(property);
        OnPropertyChanged(property + "Color");
        OnPropertyChanged(nameof(HasContrastWarning));
        OnPropertyChanged(nameof(ContrastMessage));
        if (Enabled) BeginThemeTransition();
    }

    private static Color ParseColor(string value)
    {
        try { return Color.Parse(value); }
        catch { return Colors.Black; }
    }

    private void SaveAndApplyAnimated()
    {
        _cfg.CommitConfig();
        BeginThemeTransition();
    }

    private void LoadBackground()
    {
        try
        {
            _backgroundBitmap?.Dispose();
            var path = _cfg.GetCVar(CVars.CustomThemeImage);
            _backgroundAnimation = File.Exists(path) && IsAnimatedImage(path)
                ? new AnimatedImageSourceUri(new Uri(path))
                : null;
            _backgroundBitmap = File.Exists(path) && _backgroundAnimation == null ? new Bitmap(path) : null;
        }
        catch { _backgroundBitmap = null; _backgroundAnimation = null; }
        OnPropertyChanged(nameof(BackgroundBitmap));
        OnPropertyChanged(nameof(BackgroundAnimation));
        OnPropertyChanged(nameof(BackgroundIsAnimated));
        OnPropertyChanged(nameof(BackgroundVisible));
        LoadTabBackgrounds();
        _main.RefreshThemeVisuals();
    }

    private void LoadTabBackgrounds()
    {
        foreach (var bitmap in _tabBackgrounds.Values) bitmap?.Dispose();
        _tabBackgrounds.Clear();
        foreach (var key in new[] { "home", "servers", "options" })
        {
            try
            {
                var path = _cfg.GetCVar(GetTabBackgroundCVar(key));
                _tabBackgrounds[key] = File.Exists(path) && !IsAnimatedImage(path) ? new Bitmap(path) : null;
            }
            catch { _tabBackgrounds[key] = null; }
        }
        foreach (var option in TabBackgroundOptions) option.Refresh();
        OnPropertyChanged(nameof(BackgroundVisible));
    }

    internal bool HasTabBackground(string key) => File.Exists(_cfg.GetCVar(GetTabBackgroundCVar(key)));

    internal async void ChooseTabBackground(string key)
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Выберите фон вкладки", AllowMultiple = false, FileTypeFilter = [ThemeImageFileType] });
        var source = files.FirstOrDefault()?.TryGetLocalPath();
        if (source == null) return;
        try
        {
            var destination = Path.Combine(LauncherPaths.DirUserData, $"custom-theme-{key}{Path.GetExtension(source).ToLowerInvariant()}");
            File.Copy(source, destination, true);
            ClearInstalledWorkshopTheme();
            _cfg.SetCVar(GetTabBackgroundCVar(key), destination);
            _cfg.CommitConfig();
            LoadTabBackgrounds();
            _main.RefreshThemeVisuals();
        }
        catch { _main.ShowToast("Не удалось установить фон вкладки", true); }
    }

    internal void RemoveTabBackground(string key)
    {
        ClearInstalledWorkshopTheme();
        _cfg.SetCVar(GetTabBackgroundCVar(key), "");
        _cfg.CommitConfig();
        LoadTabBackgrounds();
        _main.RefreshThemeVisuals();
    }

    private static CVarDef<string> GetTabBackgroundCVar(string key) => key switch
    {
        "home" => CVars.CustomThemeImageHome,
        "servers" => CVars.CustomThemeImageServers,
        "options" => CVars.CustomThemeImageOptions,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private static CVarDef<string>? TryGetTabBackgroundCVar(string key) => key switch
    {
        "home" => CVars.CustomThemeImageHome,
        "servers" => CVars.CustomThemeImageServers,
        "options" => CVars.CustomThemeImageOptions,
        _ => null
    };

    private static readonly FilePickerFileType ThemeImageFileType = new("Изображения и GIF")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"]
    };

    private async void BeginThemeTransition()
    {
        _transitionCancellation?.Cancel();
        var cancellation = _transitionCancellation = new CancellationTokenSource();
        ThemeTransitionOpacity = 0.72;
        try { await Task.Delay(110, cancellation.Token); }
        catch (OperationCanceledException) { return; }
        App.ApplyConfiguredTheme(_cfg);
        ThemeTransitionOpacity = 0;
    }

    public void FixContrast()
    {
        var background = ParseColor(Background);
        Text = CalculateContrast(Colors.White, background) >= CalculateContrast(Colors.Black, background)
            ? "#FFFFFF" : "#000000";
        _main.ShowToast("Контраст текста исправлен");
    }

    private static double CalculateContrast(Color first, Color second)
    {
        static double L(Color c)
        {
            static double Channel(byte value)
            {
                var x = value / 255d;
                return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
        var a = L(first); var b = L(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    public bool IsLibraryEmpty => ThemeLibrary.Count == 0;

    public bool WorkshopBusy { get => _workshopBusy; private set => SetProperty(ref _workshopBusy, value); }
    public string WorkshopStatus { get => _workshopStatus; private set => SetProperty(ref _workshopStatus, value); }
    public bool IsWorkshopEmpty => WorkshopThemes.Count == 0;
    public bool HasSelectedWorkshopTheme => _selectedWorkshopTheme != null;
    public WorkshopThemeItemViewModel? SelectedWorkshopTheme
    {
        get => _selectedWorkshopTheme;
        private set
        {
            if (!SetProperty(ref _selectedWorkshopTheme, value)) return;
            OnPropertyChanged(nameof(HasSelectedWorkshopTheme));
        }
    }
    public string PublishName { get => _publishName; set => SetProperty(ref _publishName, value); }
    public string PublishDescription { get => _publishDescription; set => SetProperty(ref _publishDescription, value); }
    public bool IsEditingWorkshopTheme => _editingWorkshopTheme != null;
    public string PublishPanelTitle => IsEditingWorkshopTheme ? "РЕДАКТИРОВАНИЕ ПУБЛИКАЦИИ" : "ОПУБЛИКОВАТЬ ТЕКУЩУЮ ТЕМУ";
    public string PublishActionText => IsEditingWorkshopTheme ? "Сохранить изменения" : "Опубликовать";
    public Bitmap? PublishIcon { get => _publishIcon; private set => SetProperty(ref _publishIcon, value); }
    public Bitmap? PublishScreenshot { get => _publishScreenshot; private set => SetProperty(ref _publishScreenshot, value); }
    public string NewComment { get => _newComment; set => SetProperty(ref _newComment, value); }
    public string WorkshopSearch
    {
        get => _workshopSearch;
        set { if (SetProperty(ref _workshopSearch, value)) ApplyWorkshopFilter(); }
    }
    public int WorkshopSortIndex
    {
        get => _workshopSortIndex;
        set { if (SetProperty(ref _workshopSortIndex, value)) ApplyWorkshopFilter(); }
    }
    public bool WorkshopFavoritesOnly
    {
        get => _workshopFavoritesOnly;
        set { if (SetProperty(ref _workshopFavoritesOnly, value)) ApplyWorkshopFilter(); }
    }
    public bool IsWorkshopPreviewActive => _previewWorkshopTheme != null;
    public string WorkshopPreviewText => _previewWorkshopTheme == null ? "" : $"Предпросмотр: {_previewWorkshopTheme.Name}";
    public bool IsWorkshopDeleteConfirmationVisible => _pendingDeleteWorkshopTheme != null;
    public string WorkshopDeleteConfirmationText => _pendingDeleteWorkshopTheme == null ? "" :
        $"Удалить тему «{_pendingDeleteWorkshopTheme.Name}»? Лайки и комментарии также будут удалены.";

    public async void OpenWorkshop()
    {
        if (_workshopWindow is { IsVisible: true })
        {
            _workshopWindow.Activate();
            return;
        }

        _workshopWindow = new ThemeWorkshopWindow { DataContext = this };
        _workshopWindow.Closed += (_, _) => _workshopWindow = null;
        if (_main.Control is { } owner) _workshopWindow.Show(owner);
        else _workshopWindow.Show();
        await RefreshWorkshopAsync();
    }

    public async void OpenWorkshopThemeById(Guid themeId)
    {
        OpenWorkshop();
        for (var attempt = 0; attempt < 100 && WorkshopBusy; attempt++) await Task.Delay(50);
        if (_allWorkshopThemes.All(x => x.Id != themeId)) await RefreshWorkshopAsync();
        var item = WorkshopThemes.FirstOrDefault(x => x.Theme.Id == themeId);
        if (item != null) await OpenWorkshopThemeAsync(item);
    }

    public async void EditWorkshopThemeById(Guid themeId)
    {
        OpenWorkshop();
        for (var attempt = 0; attempt < 100 && WorkshopBusy; attempt++) await Task.Delay(50);
        if (_allWorkshopThemes.All(x => x.Id != themeId)) await RefreshWorkshopAsync();
        var item = WorkshopThemes.FirstOrDefault(x => x.Theme.Id == themeId);
        if (item is { IsOwn: true }) UpdateWorkshopTheme(item);
    }

    public async void RefreshWorkshop() => await RefreshWorkshopAsync();

    public void OpenPublishWindow()
    {
        if (_main.ActiveAccount == null) { _main.ShowToast("Сначала войдите в аккаунт SS14", true); return; }
        if (_publishWindow != null) { _publishWindow.Activate(); return; }
        _publishWindow = new ThemePublishWindow { DataContext = this };
        _publishWindow.Closed += (_, _) => _publishWindow = null;
        if (_workshopWindow is { } owner) _publishWindow.Show(owner); else _publishWindow.Show();
    }

    public async void ChoosePublishIcon()
    {
        if (_publishWindow == null) return;
        var files = await _publishWindow.StorageProvider.OpenFilePickerAsync(ImagePicker("Выберите иконку темы", false));
        var file = files.FirstOrDefault(); if (file == null) return;
        try { PublishIcon?.Dispose(); PublishIcon = new Bitmap(await file.OpenReadAsync()); }
        catch { _main.ShowToast("Не удалось открыть иконку", true); }
    }

    public async void ChoosePublishScreenshots()
    {
        if (_publishWindow == null) return;
        var files = await _publishWindow.StorageProvider.OpenFilePickerAsync(ImagePicker("Выберите до четырёх скриншотов", true));
        foreach (var old in PublishScreenshots) old.Dispose();
        PublishScreenshots.Clear(); _publishScreenshotBytes = null; PublishScreenshot?.Dispose(); PublishScreenshot = null;
        foreach (var file in files.Take(4))
        {
            try
            {
                await using var input = await file.OpenReadAsync(); using var memory = new MemoryStream(); await input.CopyToAsync(memory);
                var bytes = memory.ToArray(); var item = new PublishScreenshotItem(file.Name, bytes); PublishScreenshots.Add(item);
                if (_publishScreenshotBytes == null) { _publishScreenshotBytes = bytes; PublishScreenshot = new Bitmap(new MemoryStream(bytes)); }
            }
            catch { }
        }
        OnPropertyChanged(nameof(PublishScreenshots));
    }

    private static FilePickerOpenOptions ImagePicker(string title, bool multiple) => new()
    { Title = title, AllowMultiple = multiple, FileTypeFilter = [new FilePickerFileType("Изображения") { Patterns = ["*.png", "*.jpg", "*.jpeg"] }] };

    private async Task RefreshWorkshopAsync()
    {
        if (WorkshopBusy) return;
        WorkshopBusy = true;
        WorkshopStatus = "Загрузка мастерской…";
        try
        {
            var themes = await _workshop.GetThemesAsync(_main.ActiveAccount?.UserId);
            _allWorkshopThemes.Clear();
            _allWorkshopThemes.AddRange(themes);
            ApplyWorkshopFilter();
            WorkshopStatus = themes.Count == 0 ? "В мастерской пока нет тем." : $"Тем в мастерской: {themes.Count}";
            OnPropertyChanged(nameof(IsWorkshopEmpty));
        }
        catch (Exception exception)
        {
            WorkshopStatus = exception.Message;
        }
        finally { WorkshopBusy = false; }
    }

    public async void PublishToWorkshop()
    {
        var account = _main.ActiveAccount;
        if (account == null) { _main.ShowToast("Сначала войдите в аккаунт SS14", true); return; }
        if (string.IsNullOrWhiteSpace(PublishName)) { _main.ShowToast("Введите название темы", true); return; }
        if (PublishName.Trim().Length > 60 || PublishDescription.Trim().Length > 2000)
        { _main.ShowToast("Название или описание слишком длинное", true); return; }
        WorkshopBusy = true;
        try
        {
            if (_editingWorkshopTheme is { } editing)
            {
                var request = new WorkshopPublishRequest(editing.Theme.Id, account.UserId, account.Username,
                    PublishName, PublishDescription, NextThemeVersion(editing.Theme.Version),
                    Background, Surface, Accent, Text, Blur);
                await _workshop.UpdateAsync(editing.Theme, request, CreateThemeArchiveBytes(), _publishScreenshotBytes ?? CreateWorkshopPreviewBytes());
                _main.ShowToast($"Тема «{PublishName.Trim()}» обновлена");
            }
            else
            {
                var request = new WorkshopPublishRequest(Guid.NewGuid(), account.UserId, account.Username,
                    PublishName, PublishDescription, "1.0", Background, Surface, Accent, Text, Blur);
                await _workshop.PublishAsync(request, CreateThemeArchiveBytes(), _publishScreenshotBytes ?? CreateWorkshopPreviewBytes());
                _main.ShowToast("Тема опубликована в мастерской");
            }
            CancelWorkshopEdit();
            _publishWindow?.Close();
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
        finally { WorkshopBusy = false; }
        await RefreshWorkshopAsync();
    }

    internal async void InstallWorkshopTheme(WorkshopThemeItemViewModel item)
    {
        if (WorkshopBusy) return;
        WorkshopBusy = true;
        try
        {
            var archive = await _workshop.DownloadAsync(item.Theme);
            ApplyWorkshopArchive(archive);
            SaveWorkshopThemeToLibrary(item.Theme, archive);
            _cfg.SetCVar(CVars.InstalledWorkshopThemeId, item.Theme.Id.ToString("D"));
            _cfg.CommitConfig();
            foreach (var theme in WorkshopThemes) theme.RefreshInstalled();
            _main.ShowToast($"Тема «{item.Name}» установлена и сохранена в библиотеке");
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
        finally { WorkshopBusy = false; }
        await RefreshWorkshopAsync();
    }

    internal bool IsWorkshopThemeInstalled(Guid themeId) =>
        Guid.TryParse(_cfg.GetCVar(CVars.InstalledWorkshopThemeId), out var installed) && installed == themeId;

    private void ClearInstalledWorkshopTheme()
    {
        if (string.IsNullOrEmpty(_cfg.GetCVar(CVars.InstalledWorkshopThemeId))) return;
        _cfg.SetCVar(CVars.InstalledWorkshopThemeId, "");
        foreach (var theme in WorkshopThemes) theme.RefreshInstalled();
    }

    internal async void ToggleWorkshopLike(WorkshopThemeItemViewModel item)
    {
        var account = _main.ActiveAccount;
        if (account == null) { _main.ShowToast("Сначала войдите в аккаунт SS14", true); return; }
        try
        {
            await _workshop.SetLikeAsync(item.Theme.Id, account.UserId, !item.IsLiked);
            await RefreshWorkshopAsync();
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
    }

    internal async void ToggleWorkshopFavorite(WorkshopThemeItemViewModel item)
    {
        var account = _main.ActiveAccount;
        if (account == null) { _main.ShowToast("Сначала войдите в аккаунт SS14", true); return; }
        try
        {
            await _workshop.SetFavoriteAsync(item.Theme.Id, account.UserId, !item.IsFavorite);
            await RefreshWorkshopAsync();
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
    }

    internal async void UpdateWorkshopTheme(WorkshopThemeItemViewModel item)
    {
        var account = _main.ActiveAccount;
        if (account == null || item.Theme.AuthorUserId != account.UserId) return;
        _editingWorkshopTheme = item;
        PublishName = item.Theme.Name;
        PublishDescription = item.Theme.Description;
        OnPropertyChanged(nameof(IsEditingWorkshopTheme));
        OnPropertyChanged(nameof(PublishPanelTitle));
        OnPropertyChanged(nameof(PublishActionText));
        try
        {
            var cover = await _workshop.DownloadPreviewAsync(item.Theme);
            if (cover != null)
            {
                _publishScreenshotBytes = cover;
                PublishScreenshot?.Dispose();
                PublishScreenshot = new Bitmap(new MemoryStream(cover));
                foreach (var screenshot in PublishScreenshots) screenshot.Dispose();
                PublishScreenshots.Clear();
                PublishScreenshots.Add(new PublishScreenshotItem("Текущая обложка", cover));
            }
        }
        catch { }
        CloseWorkshopTheme();
        OpenPublishWindow();
    }

    public void CancelWorkshopEdit()
    {
        _editingWorkshopTheme = null;
        PublishName = "";
        PublishDescription = "";
        OnPropertyChanged(nameof(IsEditingWorkshopTheme));
        OnPropertyChanged(nameof(PublishPanelTitle));
        OnPropertyChanged(nameof(PublishActionText));
        PublishIcon?.Dispose(); PublishIcon = null;
        PublishScreenshot?.Dispose(); PublishScreenshot = null; _publishScreenshotBytes = null;
        foreach (var screenshot in PublishScreenshots) screenshot.Dispose(); PublishScreenshots.Clear();
    }

    internal void RequestDeleteWorkshopTheme(WorkshopThemeItemViewModel item)
    {
        if (!item.IsOwn) return;
        _pendingDeleteWorkshopTheme = item;
        OnPropertyChanged(nameof(IsWorkshopDeleteConfirmationVisible));
        OnPropertyChanged(nameof(WorkshopDeleteConfirmationText));
    }

    public void CancelDeleteWorkshopTheme()
    {
        _pendingDeleteWorkshopTheme = null;
        OnPropertyChanged(nameof(IsWorkshopDeleteConfirmationVisible));
        OnPropertyChanged(nameof(WorkshopDeleteConfirmationText));
    }

    public async void ConfirmDeleteWorkshopTheme()
    {
        var item = _pendingDeleteWorkshopTheme;
        CancelDeleteWorkshopTheme();
        if (item == null) return;
        var account = _main.ActiveAccount;
        if (account == null || item.Theme.AuthorUserId != account.UserId) return;
        try
        {
            await _workshop.DeleteThemeAsync(item.Theme, account.UserId);
            if (IsWorkshopThemeInstalled(item.Theme.Id)) ClearInstalledWorkshopTheme();
            CloseWorkshopTheme();
            _main.ShowToast($"Тема «{item.Name}» удалена");
            await RefreshWorkshopAsync();
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
    }

    internal async void PreviewWorkshopTheme(WorkshopThemeItemViewModel item)
    {
        if (WorkshopBusy) return;
        if (_previewWorkshopTheme != null) RevertWorkshopPreview();
        WorkshopBusy = true;
        try
        {
            _previewRestoreArchive = CreateThemeArchiveBytes();
            _previewRestoreInstalledId = _cfg.GetCVar(CVars.InstalledWorkshopThemeId);
            ApplyWorkshopArchive(await _workshop.DownloadAsync(item.Theme));
            _previewWorkshopTheme = item;
            OnPropertyChanged(nameof(IsWorkshopPreviewActive));
            OnPropertyChanged(nameof(WorkshopPreviewText));
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
        finally { WorkshopBusy = false; }
    }

    public void KeepWorkshopPreview()
    {
        if (_previewWorkshopTheme == null) return;
        _cfg.SetCVar(CVars.InstalledWorkshopThemeId, _previewWorkshopTheme.Theme.Id.ToString("D"));
        _cfg.CommitConfig();
        _previewRestoreArchive = null;
        _previewWorkshopTheme = null;
        OnPropertyChanged(nameof(IsWorkshopPreviewActive));
        OnPropertyChanged(nameof(WorkshopPreviewText));
        foreach (var theme in WorkshopThemes) theme.RefreshInstalled();
        _main.ShowToast("Тема оставлена и отмечена установленной");
    }

    public void RevertWorkshopPreview()
    {
        if (_previewRestoreArchive != null) ApplyWorkshopArchive(_previewRestoreArchive);
        _cfg.SetCVar(CVars.InstalledWorkshopThemeId, _previewRestoreInstalledId);
        _cfg.CommitConfig();
        _previewRestoreArchive = null;
        _previewWorkshopTheme = null;
        OnPropertyChanged(nameof(IsWorkshopPreviewActive));
        OnPropertyChanged(nameof(WorkshopPreviewText));
        foreach (var theme in WorkshopThemes) theme.RefreshInstalled();
        _main.ShowToast("Предыдущая тема восстановлена");
    }

    internal async void OpenWorkshopTheme(WorkshopThemeItemViewModel item) => await OpenWorkshopThemeAsync(item);

    private async Task OpenWorkshopThemeAsync(WorkshopThemeItemViewModel item)
    {
        SelectedWorkshopTheme = item;
        NewComment = "";
        await LoadCommentsAsync(item.Theme.Id);
    }

    public void CloseWorkshopTheme()
    {
        SelectedWorkshopTheme = null;
        WorkshopComments.Clear();
    }

    public async void AddWorkshopComment()
    {
        var account = _main.ActiveAccount;
        if (account == null) { _main.ShowToast("Сначала войдите в аккаунт SS14", true); return; }
        if (SelectedWorkshopTheme == null || string.IsNullOrWhiteSpace(NewComment)) return;
        if (NewComment.Trim().Length > 1000) { _main.ShowToast("Комментарий длиннее 1000 символов", true); return; }
        try
        {
            await _workshop.AddCommentAsync(SelectedWorkshopTheme.Theme.Id, account.UserId, account.Username, NewComment);
            NewComment = "";
            await LoadCommentsAsync(SelectedWorkshopTheme.Theme.Id);
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
    }

    private async Task LoadCommentsAsync(Guid themeId)
    {
        try
        {
            var comments = await _workshop.GetCommentsAsync(themeId);
            WorkshopComments.Clear();
            foreach (var comment in comments) WorkshopComments.Add(new WorkshopCommentItemViewModel(this, comment, _main.ActiveAccount?.UserId));
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
    }

    internal async void DeleteWorkshopComment(WorkshopCommentItemViewModel item)
    {
        var account = _main.ActiveAccount;
        if (account == null || !item.IsOwn || SelectedWorkshopTheme == null) return;
        try
        {
            await _workshop.DeleteCommentAsync(item.Comment.Id, account.UserId);
            await LoadCommentsAsync(SelectedWorkshopTheme.Theme.Id);
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
    }

    internal bool IsOwnWorkshopTheme(WorkshopThemeDto theme) => _main.ActiveAccount?.UserId == theme.AuthorUserId;
    internal Task<byte[]?> LoadWorkshopPreviewAsync(WorkshopThemeDto theme) => _workshop.DownloadPreviewAsync(theme);

    private byte[] CreateWorkshopPreviewBytes() => ThemePreviewRenderer.Render(Background, Surface, Control,
        Accent, Text, Muted, Blur, Dimming, _cfg.GetCVar(CVars.CustomThemeImage));

    private static string NextThemeVersion(string current)
    {
        if (!Version.TryParse(current, out var version)) return "1.1";
        return $"{Math.Max(1, version.Major)}.{version.Minor + 1}";
    }

    private void ApplyWorkshopFilter()
    {
        IEnumerable<WorkshopThemeDto> themes = _allWorkshopThemes;
        if (!string.IsNullOrWhiteSpace(WorkshopSearch))
        {
            var search = WorkshopSearch.Trim();
            themes = themes.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                                       x.AuthorName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        if (WorkshopFavoritesOnly) themes = themes.Where(x => x.IsFavorite);
        themes = WorkshopSortIndex switch
        {
            1 => themes.OrderByDescending(x => x.LikeCount),
            2 => themes.OrderByDescending(x => x.Downloads),
            3 => themes.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => themes.OrderByDescending(x => x.UpdatedAt)
        };
        foreach (var old in WorkshopThemes) old.Dispose();
        WorkshopThemes.Clear();
        foreach (var theme in themes) WorkshopThemes.Add(new WorkshopThemeItemViewModel(this, theme));
        OnPropertyChanged(nameof(IsWorkshopEmpty));
    }

    private byte[] CreateThemeArchiveBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"orbitra-theme-{Guid.NewGuid():N}.zip");
        try { WriteThemeArchive(path); return File.ReadAllBytes(path); }
        finally { try { File.Delete(path); } catch { } }
    }

    private void ApplyWorkshopArchive(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"orbitra-theme-{Guid.NewGuid():N}.zip");
        try { File.WriteAllBytes(path, bytes); ApplyPackage(ReadThemeArchive(path)); }
        finally { try { File.Delete(path); } catch { } }
    }

    public void SaveToLibrary()
    {
        try
        {
            var directory = Path.Combine(LauncherPaths.DirUserData, "Themes");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"Тема {DateTime.Now:yyyy-MM-dd HH-mm-ss}.zip");
            WriteThemeArchive(path);
            LoadThemeLibrary();
            _main.ShowToast("Тема сохранена в библиотеку");
        }
        catch { _main.ShowToast("Не удалось сохранить тему", true); }
    }

    private void LoadThemeLibrary()
    {
        var directory = Path.Combine(LauncherPaths.DirUserData, "Themes");
        Directory.CreateDirectory(directory);
        ThemeLibrary.Clear();
        foreach (var path in Directory.EnumerateFiles(directory, "*.zip").OrderByDescending(File.GetLastWriteTimeUtc))
            ThemeLibrary.Add(new ThemeLibraryItemViewModel(this, path));
        OnPropertyChanged(nameof(IsLibraryEmpty));
    }

    private void SaveWorkshopThemeToLibrary(WorkshopThemeDto theme, byte[] archive)
    {
        var directory = Path.Combine(LauncherPaths.DirUserData, "Themes");
        Directory.CreateDirectory(directory);
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(theme.Name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "Тема мастерской";
        if (safeName.Length > 64) safeName = safeName[..64];
        var path = Path.Combine(directory, $"Workshop · {safeName} · {theme.Id:N}.zip");
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, archive);
        File.Move(temporary, path, true);
        LoadThemeLibrary();
    }

    internal void ApplyLibraryTheme(string path)
    {
        try
        {
            ApplyPackage(ReadThemeArchive(path));
            _main.ShowToast("Тема из библиотеки применена");
        }
        catch { _main.ShowToast("Не удалось открыть тему из библиотеки", true); }
    }

    internal void DeleteLibraryTheme(string path)
    {
        try { File.Delete(path); LoadThemeLibrary(); }
        catch { _main.ShowToast("Не удалось удалить тему", true); }
    }
}

public sealed class ThemeBackgroundOptionViewModel : ObservableObject
{
    private readonly CustomThemeTabViewModel _owner;
    private readonly string _key;
    public string Name { get; }
    public bool HasBackground => _owner.HasTabBackground(_key);
    public ThemeBackgroundOptionViewModel(CustomThemeTabViewModel owner, string key, string name)
    { _owner = owner; _key = key; Name = name; }
    public void Choose() => _owner.ChooseTabBackground(_key);
    public void Remove() => _owner.RemoveTabBackground(_key);
    internal void Refresh() => OnPropertyChanged(nameof(HasBackground));
}

public sealed class ThemeLibraryItemViewModel
{
    private readonly CustomThemeTabViewModel _owner;
    private readonly string _path;
    public string Name => Path.GetFileNameWithoutExtension(_path);
    public string Date => File.GetLastWriteTime(_path).ToString("dd.MM.yyyy · HH:mm");
    public bool IsWorkshopTheme => Name.StartsWith("Workshop · ", StringComparison.Ordinal);
    public string Source => IsWorkshopTheme ? "СКАЧАНО ИЗ МАСТЕРСКОЙ" : "ЛОКАЛЬНАЯ ТЕМА";
    public ThemeLibraryItemViewModel(CustomThemeTabViewModel owner, string path)
    { _owner = owner; _path = path; }
    public void Apply() => _owner.ApplyLibraryTheme(_path);
    public void Delete() => _owner.DeleteLibraryTheme(_path);
}

public sealed class WorkshopThemeItemViewModel : ObservableObject, IDisposable
{
    private readonly CustomThemeTabViewModel _owner;
    public WorkshopThemeDto Theme { get; }
    public string Name => Theme.Name;
    public string Description => Theme.Description;
    public string Author => $"Автор: {Theme.AuthorName}";
    public string Version => $"v{Theme.Version}";
    public string Date => Theme.CreatedAt.LocalDateTime.ToString("dd.MM.yyyy");
    public string Stats => $"♥ {Theme.LikeCount}   ↓ {Theme.Downloads}   Комментарии: {Theme.CommentCount}";
    public string LikeText => Theme.IsLiked ? "Убрать лайк" : "Нравится";
    public bool IsLiked => Theme.IsLiked;
    public bool IsFavorite => Theme.IsFavorite;
    public string FavoriteText => IsFavorite ? "В избранном" : "В избранное";
    public bool IsOwn => _owner.IsOwnWorkshopTheme(Theme);
    public bool IsInstalled => _owner.IsWorkshopThemeInstalled(Theme.Id);
    public bool CanInstall => !IsInstalled;
    public string InstallText => IsInstalled ? "Установлено" : "Установить";
    public string Palette => $"{Theme.Background}  {Theme.Surface}  {Theme.Accent}  {Theme.TextColor}";
    private Bitmap? _previewBitmap;
    public Bitmap? PreviewBitmap { get => _previewBitmap; private set => SetProperty(ref _previewBitmap, value); }
    public WorkshopThemeItemViewModel(CustomThemeTabViewModel owner, WorkshopThemeDto theme)
    { _owner = owner; Theme = theme; _ = LoadPreviewAsync(); }
    public void Install() => _owner.InstallWorkshopTheme(this);
    public void Preview() => _owner.PreviewWorkshopTheme(this);
    public void ToggleLike() => _owner.ToggleWorkshopLike(this);
    public void ToggleFavorite() => _owner.ToggleWorkshopFavorite(this);
    public void Update() => _owner.UpdateWorkshopTheme(this);
    public void Delete() => _owner.RequestDeleteWorkshopTheme(this);
    public void Open() => _owner.OpenWorkshopTheme(this);
    private async Task LoadPreviewAsync()
    {
        try
        {
            var bytes = await _owner.LoadWorkshopPreviewAsync(Theme);
            if (bytes != null) PreviewBitmap = new Bitmap(new MemoryStream(bytes));
        }
        catch { }
    }
    internal void RefreshInstalled()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallText));
    }
    public void Dispose() { PreviewBitmap?.Dispose(); PreviewBitmap = null; }
}

public sealed class WorkshopCommentItemViewModel
{
    private readonly CustomThemeTabViewModel _owner;
    internal WorkshopCommentDto Comment { get; }
    public string Author => Comment.UserName;
    public string Content => Comment.Content;
    public string Date => Comment.CreatedAt.LocalDateTime.ToString("dd.MM.yyyy · HH:mm");
    public bool IsOwn { get; }
    public WorkshopCommentItemViewModel(CustomThemeTabViewModel owner, WorkshopCommentDto comment, Guid? activeUser)
    { _owner = owner; Comment = comment; IsOwn = activeUser == comment.UserId; }
    public void Delete() => _owner.DeleteWorkshopComment(this);
}

public sealed class PublishScreenshotItem : IDisposable
{
    public string Name { get; }
    public Bitmap Bitmap { get; }
    public PublishScreenshotItem(string name, byte[] bytes) { Name = name; Bitmap = new Bitmap(new MemoryStream(bytes)); }
    public void Dispose() => Bitmap.Dispose();
}
