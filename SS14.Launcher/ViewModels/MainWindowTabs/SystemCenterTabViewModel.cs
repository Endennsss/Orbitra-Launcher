using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using DynamicData;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Splat;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class SystemCenterTabViewModel : MainWindowTabViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly DataManager _cfg;
    private readonly ContentManager _contentManager;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(7) };

    public ObservableCollection<SystemCheckItem> Integrity { get; } = [];
    public ObservableCollection<SystemCheckItem> Services { get; } = [];
    public ObservableCollection<ReleaseHistoryItem> Releases { get; } = [];
    public ObservableCollection<FavoriteToolItem> FavoriteTools { get; } = [];
    public ObservableCollection<ServerContentItem> ServerContent { get; } = [];
    public IEnumerable<FavoriteToolItem> ComparedServers => FavoriteTools.Where(x => x.IsCompared);
    public string CurrentVersion => LauncherVersion.Version?.ToString() ?? "неизвестно";
    public string ContentDatabaseSize => $"Фактически на диске: {Helpers.FormatBytes(_contentManager.GetDatabaseSize())}";
    public bool HasSelectedContent => ServerContent.Any(x => x.IsSelected);

    public override string Name => "Система";
    // Lucide "monitor-cog": kept inside the standard 24x24 viewport.
    public override string IconData => "M12,17 L4,17 A2,2 0 0 1 2,15 L2,5 A2,2 0 0 1 4,3 L20,3 A2,2 0 0 1 22,5 L22,11 M8,21 L12,17 M16,21 L12,17 M19,14 L19,16 M19,20 L19,22 M15.54,16 L17.27,17 M20.73,19 L22.46,20 M15.54,20 L17.27,19 M20.73,17 L22.46,16";

    public SystemCenterTabViewModel(MainWindowViewModel main)
    {
        _main = main;
        _cfg = Locator.Current.GetRequiredService<DataManager>();
        _contentManager = Locator.Current.GetRequiredService<ContentManager>();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Orbitra-Launcher");
        _cfg.FavoriteServers.Connect().Subscribe(_ => RefreshFavorites());
        RefreshFavorites();
        RefreshServerContent();
    }

    public override void Selected()
    {
        if (Integrity.Count == 0) RunIntegrity();
        if (Services.Count == 0) RefreshServices();
        if (Releases.Count == 0) RefreshReleases();
        RefreshServerContent();
    }

    public void RefreshServerContent()
    {
        ServerContent.Clear();
        foreach (var version in _contentManager.GetManagedVersions())
            ServerContent.Add(new ServerContentItem(this, version));
        OnPropertyChanged(nameof(ContentDatabaseSize));
        OnPropertyChanged(nameof(HasSelectedContent));
    }

    public async void DeleteSelectedContent()
    {
        var selected = ServerContent.Where(x => x.IsSelected).Select(x => x.Id).ToArray();
        if (selected.Length == 0) { _main.ShowToast("Выберите хотя бы одну сборку"); return; }
        var result = await _contentManager.RemoveVersions(selected);
        _main.ShowToast(result ? "Выбранный контент удалён" : "Контент используется запущенным клиентом", !result);
        RefreshServerContent();
    }

    public async void DeleteAllContent()
    {
        var result = await _contentManager.ClearAll();
        _main.ShowToast(result ? "Кэш серверного контента очищен" : "Сначала закройте запущенный клиент", !result);
        RefreshServerContent();
    }

    internal void ContentSelectionChanged() => OnPropertyChanged(nameof(HasSelectedContent));

    public async void RunIntegrity()
    {
        Integrity.Clear();
        await AddCheck("Loader", async () => File.Exists(await SS14.Launcher.Models.Connector.GetLoaderExecutablePathAsync()), "Компонент запуска отсутствует");
        AddCheck("Runtime", Directory.Exists(Path.Combine(LauncherPaths.DirLauncherInstall, "dotnet")) ||
                            RuntimeInformation.FrameworkDescription.Contains(".NET"), "Среда .NET не обнаружена");
        AddCheck("База контента", () => { using var db = ContentManager.GetSqliteConnection(); return true; }, "База не открывается");
        AddCheck("Конфигурация", File.Exists(Path.Combine(LauncherPaths.DirUserData, "settings.db")), "settings.db отсутствует");
        AddCheck("Права записи", CanWrite(LauncherPaths.DirLocalData), "Нет прав записи в локальные данные");
        var drive = new DriveInfo(Path.GetPathRoot(LauncherPaths.DirLocalData)!);
        AddCheck("Свободное место", drive.AvailableFreeSpace >= 2L * 1024 * 1024 * 1024,
            $"Свободно {Helpers.FormatBytes(drive.AvailableFreeSpace)} · рекомендуется 2 ГБ");
        _main.ShowToast(Integrity.All(x => x.Ok) ? "Проверка целостности пройдена" : "Проверка нашла проблемы", !Integrity.All(x => x.Ok));
    }

    public async void RefreshServices()
    {
        Services.Clear();
        await Probe("GitHub Releases", ConfigConstants.CustomLatestReleaseApiUrl);
        await Probe("SS14 Hub", "https://hub.spacestation14.com/api/servers");
        await Probe("Авторизация", "https://auth.spacestation14.com/");
        await Probe("CDN", "https://launcher-data.cdn.spacestation14.com/info.json");
        Services.Add(new SystemCheckItem("Discord RPC",
            DiscordRichPresenceService.Instance.IsConnected ? "Подключено" : "Discord не запущен или RPC недоступен",
            DiscordRichPresenceService.Instance.IsConnected));
    }

    public async void RefreshReleases()
    {
        try
        {
            var json = await _http.GetStringAsync("https://api.github.com/repos/Endennsss/Orbitra-Launcher/releases?per_page=12");
            var releases = JsonSerializer.Deserialize<ReleaseDto[]>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            Releases.Clear();
            foreach (var release in releases.Where(x => !x.Draft))
                Releases.Add(new ReleaseHistoryItem(release.Name, release.TagName,
                    release.PublishedAt.ToLocalTime().ToString("dd.MM.yyyy"), release.HtmlUrl));
        }
        catch (Exception e) { _main.ShowToast($"История обновлений недоступна: {e.Message}", true); }
    }

    public async void ExportErrorReport()
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Обезличенный отчёт Orbitra", SuggestedFileName = $"orbitra-report-{DateTime.Now:yyyyMMdd-HHmm}.zip", DefaultExtension = "zip"
        });
        var path = file?.TryGetLocalPath(); if (path == null) return;
        try
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            var summary = $"Orbitra {LauncherVersion.Version}\nOS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}\nCPU: {LauncherDiagnostics.GetProcessorModel()}\nGenerated: {DateTimeOffset.Now:O}\n";
            WriteEntry(zip, "system.txt", summary);
            foreach (var log in Directory.EnumerateFiles(LauncherPaths.DirLogs).OrderByDescending(File.GetLastWriteTime).Take(6))
                WriteEntry(zip, "logs/" + Path.GetFileName(log), Redact(await File.ReadAllTextAsync(log)));
            WriteEntry(zip, "integrity.txt", string.Join('\n', Integrity.Select(x => $"{x.Title}: {x.Details}")));
            _main.ShowToast("Обезличенный отчёт создан");
        }
        catch (Exception e) { _main.ShowToast($"Не удалось создать отчёт: {e.Message}", true); }
    }

    private void RefreshFavorites()
    {
        var selected = FavoriteTools.Where(x => x.IsCompared).Select(x => x.Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        FavoriteTools.Clear();
        foreach (var server in _main.HomeTab.Favorites)
            FavoriteTools.Add(new FavoriteToolItem(this, server, selected.Contains(server.CacheData.Address)));
        OnPropertyChanged(nameof(ComparedServers));
    }

    internal bool TrySetCompared(FavoriteToolItem item, bool value)
    {
        if (value && FavoriteTools.Count(x => x.IsCompared && !ReferenceEquals(x, item)) >= 3)
        {
            _main.ShowToast("Для сравнения можно выбрать не больше трёх серверов", true);
            return false;
        }
        return true;
    }

    internal void ComparisonChanged() => OnPropertyChanged(nameof(ComparedServers));


    private async Task Probe(string title, string url)
    {
        var sw = Stopwatch.StartNew();
        try { using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead); Services.Add(new(title, $"{(int)response.StatusCode} · {sw.ElapsedMilliseconds} ms", response.IsSuccessStatusCode)); }
        catch (Exception e) { Services.Add(new(title, e.GetBaseException().Message, false)); }
    }

    private async Task AddCheck(string title, Func<Task<bool>> check, string failure)
    { try { AddCheck(title, await check(), failure); } catch (Exception e) { Integrity.Add(new(title, e.GetBaseException().Message, false)); } }
    private void AddCheck(string title, Func<bool> check, string failure)
    { try { AddCheck(title, check(), failure); } catch (Exception e) { Integrity.Add(new(title, e.GetBaseException().Message, false)); } }
    private void AddCheck(string title, bool ok, string failure) => Integrity.Add(new(title, ok ? "Готово" : failure, ok));
    private static bool CanWrite(string dir) { try { Directory.CreateDirectory(dir); var path = Path.Combine(dir, $".write-{Guid.NewGuid():N}"); File.WriteAllText(path, "ok"); File.Delete(path); return true; } catch { return false; } }
    private static void WriteEntry(ZipArchive zip, string name, string content) { using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), Encoding.UTF8); writer.Write(content); }
    private static string Redact(string value)
    {
        value = Regex.Replace(value, @"(?i)(token|authorization|password)(\s*[:=]\s*)[^\s,;]+", "$1$2[REDACTED]");
        value = Regex.Replace(value, @"\b[0-9a-fA-F]{8}-[0-9a-fA-F-]{27,}\b", "[GUID]");
        value = Regex.Replace(value, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[IP]");
        value = Regex.Replace(value, @"(?i)(--username\s+)[^\s]+", "$1[USER]");
        return value.Replace(Environment.UserName, "[USER]", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ReleaseDto { public string Name { get; init; } = "Релиз"; [JsonPropertyName("tag_name")] public string TagName { get; init; } = ""; [JsonPropertyName("html_url")] public string HtmlUrl { get; init; } = ""; [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; init; } public bool Draft { get; init; } }
}

public sealed record SystemCheckItem(string Title, string Details, bool Ok) { public string Marker => Ok ? "✓" : "×"; public string Color => Ok ? "#63C174" : "#D76464"; }
public sealed record ReleaseHistoryItem(string Name, string Version, string Date, string Url) { public void Open() => Helpers.OpenUri(new Uri(Url)); }

public sealed class FavoriteToolItem : ObservableObject
{
    private readonly SystemCenterTabViewModel _owner; private bool _isCompared;
    public ServerEntryViewModel Server { get; }
    public string Address => Server.CacheData.Address; public string Name => Server.Name;
    public string RoundTime => Server.RoundStartTime is { } start ? $"Раунд: {DateTime.Now - start:hh\\:mm\\:ss}" : "Раунд: —";
    public bool IsCompared { get => _isCompared; set { if (value == _isCompared) return; if (!_owner.TrySetCompared(this, value)) { OnPropertyChanged(); return; } SetProperty(ref _isCompared, value); _owner.ComparisonChanged(); } }
    public FavoriteToolItem(SystemCenterTabViewModel owner, ServerEntryViewModel server, bool selected) { _owner = owner; Server = server; _isCompared = selected; }
}

public sealed class ServerContentItem : ObservableObject
{
    private readonly SystemCenterTabViewModel _owner; private bool _isSelected;
    public long Id { get; } public string ForkId { get; } public string ForkVersion { get; }
    public string Engine { get; } public string LastUsed { get; } public string Size { get; }
    public string Files { get; } public bool InUse { get; }
    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) _owner.ContentSelectionChanged(); } }
    public ServerContentItem(SystemCenterTabViewModel owner, ManagedContentVersion item)
    {
        _owner = owner; Id = item.Id; ForkId = item.ForkId; ForkVersion = item.ForkVersion;
        Engine = item.EngineVersion; LastUsed = item.LastUsed.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        Size = Helpers.FormatBytes(item.LogicalSize); Files = $"{item.FileCount:N0} файлов"; InUse = item.InUse;
    }
}
