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
    private WorkshopThemeItemViewModel? _selectedWorkshopTheme;
    private bool _workshopBusy;
    private string _workshopStatus = "Загрузка мастерской…";
    private string _publishName = "";
    private string _publishDescription = "";
    private string _newComment = "";

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
    public override string IconData => "M12,22 A1,1 0 0 1 12,2 A10,9 0 0 1 22,11 A5,5 0 0 1 17,16 L14.75,16 A1.75,1.75 0 0 0 13.35,18.8 L13.65,19.2 A1.75,1.75 0 0 1 12.25,22 Z M13,6.5 A0.5,0.5 0 1 0 14,6.5 A0.5,0.5 0 1 0 13,6.5 M17,10.5 A0.5,0.5 0 1 0 18,10.5 A0.5,0.5 0 1 0 17,10.5 M6,12.5 A0.5,0.5 0 1 0 7,12.5 A0.5,0.5 0 1 0 6,12.5 M8,7.5 A0.5,0.5 0 1 0 9,7.5 A0.5,0.5 0 1 0 8,7.5";

    public bool Enabled
    {
        get => _cfg.GetCVar(CVars.CustomThemeEnabled);
        set
        {
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
            _cfg.SetCVar(CVars.CustomThemeBlur, blur);
            _cfg.CommitConfig();
            OnPropertyChanged(nameof(Blur));
        }
    }

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
            _cfg.SetCVar(CVars.CustomThemeImage, destination);
            _cfg.CommitConfig();
            LoadBackground();
        }
        catch { _main.ShowToast("Не удалось загрузить изображение", true); }
    }

    public void RemoveBackground()
    {
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
        _cfg.SetCVar(CVars.CustomThemeBackground, "#101010");
        _cfg.SetCVar(CVars.CustomThemeSurface, "#181818");
        _cfg.SetCVar(CVars.CustomThemeControl, "#292929");
        _cfg.SetCVar(CVars.CustomThemeAccent, "#D0D0D0");
        _cfg.SetCVar(CVars.CustomThemeText, "#F2F2F2");
        _cfg.SetCVar(CVars.CustomThemeMuted, "#999999");
        _cfg.SetCVar(CVars.CustomThemeBlur, 8);
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
        var manifest = new ThemeArchive(1, Enabled, Background, Surface, Control, Accent, Text, Muted, Blur, imageName, tabs);
        var manifestEntry = archive.CreateEntry("theme.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(manifestEntry.Open());
        writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static ThemePackage ReadThemeArchive(string path)
    {
        using var archive = ZipFile.OpenRead(path);
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
    }

    private void ApplyPackage(ThemePackage package)
    {
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
        Dictionary<string, string>? TabBackgroundFiles = null);
    private sealed record ThemePackage(ThemeArchive Manifest, Dictionary<string, byte[]> Images);

    private void SetColor(CVarDef<string> cvar, string value, string property)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { Color.Parse(value.Trim()); }
        catch { return; }
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
            _cfg.SetCVar(GetTabBackgroundCVar(key), destination);
            _cfg.CommitConfig();
            LoadTabBackgrounds();
            _main.RefreshThemeVisuals();
        }
        catch { _main.ShowToast("Не удалось установить фон вкладки", true); }
    }

    internal void RemoveTabBackground(string key)
    {
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
    public string NewComment { get => _newComment; set => SetProperty(ref _newComment, value); }

    public async void RefreshWorkshop() => await RefreshWorkshopAsync();

    private async Task RefreshWorkshopAsync()
    {
        if (WorkshopBusy) return;
        WorkshopBusy = true;
        WorkshopStatus = "Загрузка мастерской…";
        try
        {
            var themes = await _workshop.GetThemesAsync(_main.ActiveAccount?.UserId);
            WorkshopThemes.Clear();
            foreach (var theme in themes) WorkshopThemes.Add(new WorkshopThemeItemViewModel(this, theme));
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
            var request = new WorkshopPublishRequest(Guid.NewGuid(), account.UserId, account.Username,
                PublishName, PublishDescription, Background, Surface, Accent, Text, Blur);
            await _workshop.PublishAsync(request, CreateThemeArchiveBytes());
            PublishName = ""; PublishDescription = "";
            _main.ShowToast("Тема опубликована в мастерской");
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
            ApplyWorkshopArchive(await _workshop.DownloadAsync(item.Theme));
            _main.ShowToast($"Тема «{item.Name}» установлена");
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
        finally { WorkshopBusy = false; }
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

    internal async void OpenWorkshopTheme(WorkshopThemeItemViewModel item)
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
            foreach (var comment in comments) WorkshopComments.Add(new WorkshopCommentItemViewModel(comment));
        }
        catch (Exception exception) { _main.ShowToast(exception.Message, true); }
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
    public ThemeLibraryItemViewModel(CustomThemeTabViewModel owner, string path)
    { _owner = owner; _path = path; }
    public void Apply() => _owner.ApplyLibraryTheme(_path);
    public void Delete() => _owner.DeleteLibraryTheme(_path);
}

public sealed class WorkshopThemeItemViewModel
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
    public string Palette => $"{Theme.Background}  {Theme.Surface}  {Theme.Accent}  {Theme.TextColor}";
    public WorkshopThemeItemViewModel(CustomThemeTabViewModel owner, WorkshopThemeDto theme)
    { _owner = owner; Theme = theme; }
    public void Install() => _owner.InstallWorkshopTheme(this);
    public void ToggleLike() => _owner.ToggleWorkshopLike(this);
    public void Open() => _owner.OpenWorkshopTheme(this);
}

public sealed class WorkshopCommentItemViewModel
{
    private readonly WorkshopCommentDto _comment;
    public string Author => _comment.UserName;
    public string Content => _comment.Content;
    public string Date => _comment.CreatedAt.LocalDateTime.ToString("dd.MM.yyyy · HH:mm");
    public WorkshopCommentItemViewModel(WorkshopCommentDto comment) => _comment = comment;
}
