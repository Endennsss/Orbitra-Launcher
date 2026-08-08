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
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Splat;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class SystemCenterTabViewModel : MainWindowTabViewModel
{
    private readonly MainWindowViewModel _main;
    private readonly ContentManager _contentManager;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(7) };

    public ObservableCollection<SystemCheckItem> Integrity { get; } = [];
    public ObservableCollection<SystemCheckItem> Services { get; } = [];
    public ObservableCollection<ReleaseHistoryItem> Releases { get; } = [];
    public ObservableCollection<ServerContentItem> ServerContent { get; } = [];
    public string CurrentVersion => LauncherVersion.Version?.ToString() ?? "неизвестно";
    public string ContentDatabaseSize => $"Фактически на диске: {Helpers.FormatBytes(_contentManager.GetDatabaseSize())}";
    public bool HasSelectedContent => ServerContent.Any(x => x.IsSelected);

    public override string Name => "Система";
    // Lucide "monitor-cog": kept inside the standard 24x24 viewport.
    public override string IconData => "M4,3 L20,3 A2,2 0 0 1 22,5 L22,15 A2,2 0 0 1 20,17 L4,17 A2,2 0 0 1 2,15 L2,5 A2,2 0 0 1 4,3 Z M8,21 L16,21 M12,17 L12,21";

    public SystemCenterTabViewModel(MainWindowViewModel main)
    {
        _main = main;
        _contentManager = Locator.Current.GetRequiredService<ContentManager>();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Orbitra-Launcher");
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
        var probes = await Task.WhenAll(
            Probe("Обновления Orbitra", ConfigConstants.CustomLatestReleaseApiUrl),
            Probe("Список серверов", "https://hub.spacestation14.com/api/servers"),
            Probe("Метаданные лаунчера", "https://launcher-data.cdn.spacestation14.com/info.json"),
            Probe("Сборки клиента", "https://robust-builds.cdn.spacestation14.com/manifest.json"),
            Probe("Новости Orbitra", ConfigConstants.LauncherNewsUrl),
            Probe("Профили и мастерская",
                "https://lvhysaqgxynjcfavrvui.supabase.co/rest/v1/workshop_themes?select=id&limit=1",
                "sb_publishable_-MjoEbdhEVaP1QsIrPcbIA_BxqxLw5j"));

        foreach (var result in probes)
            Services.Add(result);

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
            {
                var asset = release.Assets.FirstOrDefault(x =>
                    x.Name.Equals("Orbitra_Launcher_Windows.zip", StringComparison.OrdinalIgnoreCase));
                Releases.Add(new ReleaseHistoryItem(this, release.Name, release.TagName,
                    release.PublishedAt.ToLocalTime().ToString("dd.MM.yyyy"), release.HtmlUrl,
                    asset?.BrowserDownloadUrl, asset?.Digest));
            }
        }
        catch (Exception e) { _main.ShowToast($"История обновлений недоступна: {e.Message}", true); }
    }

    internal async void InstallRelease(ReleaseHistoryItem release)
    {
        if (!release.CanInstall || release.DownloadUrl == null || release.Digest == null)
            return;

        try
        {
            release.IsInstalling = true;
            release.ActionText = "Подготовка…";
            await LauncherUpdateService.StageAndRestartAsync(release.Version, release.DownloadUrl, release.Digest,
                value => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    release.ActionText = $"{value:P0}"));
            release.ActionText = "Перезапуск…";
            _main.Control?.PrepareForExit();
            _main.Control?.Close();
        }
        catch (Exception e)
        {
            release.IsInstalling = false;
            release.UpdateActionText();
            _main.ShowToast($"Не удалось установить {release.Version}: {e.Message}", true);
        }
    }

    public async void ExportErrorReport()
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Обезличенный отчёт Orbitra", SuggestedFileName = $"orbitra-report-{DateTime.Now:yyyyMMdd-HHmm}.zip", DefaultExtension = "zip"
        });
        if (file == null) return;
        try
        {
            await using var output = await file.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            var summary = $"Orbitra {LauncherVersion.Version}\nOS: {RuntimeInformation.OSDescription}\nRuntime: {RuntimeInformation.FrameworkDescription}\nCPU: {LauncherDiagnostics.GetProcessorModel()}\nGenerated: {DateTimeOffset.Now:O}\n";
            WriteEntry(zip, "system.txt", summary);
            if (Directory.Exists(LauncherPaths.DirLogs))
            {
                foreach (var log in Directory.EnumerateFiles(LauncherPaths.DirLogs).OrderByDescending(File.GetLastWriteTimeUtc).Take(6))
                {
                    try { WriteEntry(zip, "logs/" + Path.GetFileName(log), Redact(await File.ReadAllTextAsync(log))); }
                    catch (IOException) { WriteEntry(zip, "logs/" + Path.GetFileName(log) + ".unavailable.txt", "Файл используется другим процессом."); }
                    catch (UnauthorizedAccessException) { WriteEntry(zip, "logs/" + Path.GetFileName(log) + ".unavailable.txt", "Нет доступа к файлу."); }
                }
            }
            WriteEntry(zip, "integrity.txt", string.Join('\n', Integrity.Select(x => $"{x.Title}: {x.Details}")));
            WriteEntry(zip, "services.txt", string.Join('\n', Services.Select(x => $"{x.Title}: {x.Details}")));
            _main.ShowToast("Обезличенный отчёт создан");
        }
        catch (Exception e) { _main.ShowToast($"Не удалось создать отчёт: {e.Message}", true); }
    }

    private async Task<SystemCheckItem> Probe(string title, string url, string? apiKey = null)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (apiKey != null)
                request.Headers.Add("apikey", apiKey);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            return new(title, $"{(int)response.StatusCode} · {sw.ElapsedMilliseconds} ms", response.IsSuccessStatusCode);
        }
        catch (TaskCanceledException)
        {
            return new(title, "Тайм-аут подключения", false);
        }
        catch (Exception e)
        {
            return new(title, e.GetBaseException().Message, false);
        }
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

    private sealed class ReleaseDto
    {
        public string Name { get; init; } = "Релиз";
        [JsonPropertyName("tag_name")] public string TagName { get; init; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; init; } = "";
        [JsonPropertyName("published_at")] public DateTimeOffset PublishedAt { get; init; }
        public bool Draft { get; init; }
        public ReleaseAssetDto[] Assets { get; init; } = [];
    }
    private sealed class ReleaseAssetDto
    {
        public string Name { get; init; } = "";
        [JsonPropertyName("browser_download_url")] public string BrowserDownloadUrl { get; init; } = "";
        public string Digest { get; init; } = "";
    }
}

public sealed record SystemCheckItem(string Title, string Details, bool Ok) { public string Marker => Ok ? "✓" : "×"; public string Color => Ok ? "#63C174" : "#D76464"; }
public sealed class ReleaseHistoryItem : ObservableObject
{
    private readonly SystemCenterTabViewModel _owner;
    private bool _isInstalling;
    private string _actionText = "";
    public string Name { get; }
    public string Version { get; }
    public string Date { get; }
    public string Url { get; }
    public string? DownloadUrl { get; }
    public string? Digest { get; }
    public bool IsCurrent { get; }
    public bool HasPackage => !string.IsNullOrWhiteSpace(DownloadUrl) &&
                              !string.IsNullOrWhiteSpace(Digest) &&
                              Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase);
    public bool CanInstall => HasPackage && !IsCurrent && !IsInstalling;
    public bool IsInstalling { get => _isInstalling; set { if (SetProperty(ref _isInstalling, value)) OnPropertyChanged(nameof(CanInstall)); } }
    public string ActionText { get => _actionText; set => SetProperty(ref _actionText, value); }

    public ReleaseHistoryItem(SystemCenterTabViewModel owner, string name, string version, string date,
        string url, string? downloadUrl, string? digest)
    {
        _owner = owner; Name = name; Version = version; Date = date; Url = url;
        DownloadUrl = downloadUrl; Digest = digest;
        IsCurrent = string.Equals(version.TrimStart('v', 'V'), LauncherVersion.Version?.ToString(), StringComparison.OrdinalIgnoreCase);
        UpdateActionText();
    }

    public void Open() => Helpers.OpenUri(new Uri(Url));
    public void Install() => _owner.InstallRelease(this);
    internal void UpdateActionText()
    {
        if (IsCurrent) { ActionText = "Текущая"; return; }
        if (!HasPackage) { ActionText = "Нет ZIP"; return; }
        if (System.Version.TryParse(Version.TrimStart('v', 'V').Split('-', 2)[0], out var target) &&
            LauncherVersion.Version is { } current && target < current)
            ActionText = "Откатить";
        else
            ActionText = "Установить";
    }
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
