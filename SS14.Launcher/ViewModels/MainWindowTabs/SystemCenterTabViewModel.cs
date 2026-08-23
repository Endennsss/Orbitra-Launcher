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
using System.Threading;
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
    private readonly string _iconCachePath = Path.Combine(LauncherPaths.DirLocalData, "server-icons");
    private readonly string _updatesPath = Path.Combine(LauncherPaths.DirLocalData, "updates");
    private readonly string _localServersPath = Path.Combine(LauncherPaths.DirUserData, "LocalServers");
    private readonly string _themesPath = Path.Combine(LauncherPaths.DirUserData, "Themes");
    private long[] _pendingContentIds = [];
    private DiskCleanupKind[] _pendingDiskKinds = [];
    private bool _isAnalyzingLogs;
    private bool _isScanningDisk;
    private bool _isCleanupPreviewVisible;
    private string _cleanupPreviewText = string.Empty;
    private bool _isDeduplicationRunning;
    private string _deduplicationStatus = "Нажмите «Анализировать», чтобы найти общие файлы между сборками.";
    private string _deduplicationSavings = "Экономия ещё не рассчитана";
    private string _deduplicationFiles = "Нет данных";
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private TimeSpan _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;

    public ObservableCollection<SystemCheckItem> Integrity { get; } = [];
    public ObservableCollection<SystemCheckItem> Services { get; } = [];
    public ObservableCollection<ReleaseHistoryItem> Releases { get; } = [];
    public ObservableCollection<ServerContentItem> ServerContent { get; } = [];
    public ObservableCollection<LaunchDiagnosticItem> LaunchDiagnostics { get; } = [];
    public ObservableCollection<DiskUsageItem> DiskUsage { get; } = [];
    public ObservableCollection<PerformanceMetricItem> PerformanceMetrics { get; } = [];
    public ObservableCollection<PerformanceOperationItem> SlowOperations { get; } = [];
    public string CurrentVersion => LauncherVersion.Version?.ToString() ?? "неизвестно";
    public string ContentDatabaseSize => $"Фактически на диске: {Helpers.FormatBytes(_contentManager.GetDatabaseSize())}";
    public bool HasSelectedContent => ServerContent.Any(x => x.IsSelected);
    public bool IsAnalyzingLogs { get => _isAnalyzingLogs; private set => SetProperty(ref _isAnalyzingLogs, value); }
    public bool IsScanningDisk { get => _isScanningDisk; private set => SetProperty(ref _isScanningDisk, value); }
    public bool IsCleanupPreviewVisible { get => _isCleanupPreviewVisible; private set => SetProperty(ref _isCleanupPreviewVisible, value); }
    public string CleanupPreviewText { get => _cleanupPreviewText; private set => SetProperty(ref _cleanupPreviewText, value); }
    public bool IsDeduplicationRunning { get => _isDeduplicationRunning; private set => SetProperty(ref _isDeduplicationRunning, value); }
    public string DeduplicationStatus { get => _deduplicationStatus; private set => SetProperty(ref _deduplicationStatus, value); }
    public string DeduplicationSavings { get => _deduplicationSavings; private set => SetProperty(ref _deduplicationSavings, value); }
    public string DeduplicationFiles { get => _deduplicationFiles; private set => SetProperty(ref _deduplicationFiles, value); }

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
        if (LaunchDiagnostics.Count == 0) AnalyzeLaunchLogs();
        ScanDisk();
        AnalyzeDuplicateContent();
        RefreshPerformance();
    }

    public async void AnalyzeDuplicateContent()
    {
        if (IsDeduplicationRunning) return;
        IsDeduplicationRunning = true;
        try
        {
            using var measurement = PerformanceTelemetry.Measure("Анализ дедупликации контента");
            var report = await Task.Run(_contentManager.AnalyzeDeduplication);
            DeduplicationSavings = report.SavedBytes > 0
                ? $"Сэкономлено {Helpers.FormatBytes(report.SavedBytes)}"
                : "Общие файлы пока не найдены";
            DeduplicationFiles = $"{report.SharedReferences:N0} повторных ссылок · {report.UniqueFiles:N0} уникальных файлов";
            DeduplicationStatus = report.OrphanedFiles > 0
                ? $"Найдено неиспользуемых объектов: {report.OrphanedFiles:N0} ({Helpers.FormatBytes(report.OrphanedBytes)}). Можно безопасно оптимизировать."
                : $"База хранит {report.ReferencedFiles:N0} файловых ссылок. Одинаковые данные между серверами уже объединены по BLAKE2b.";
        }
        catch (Exception e)
        {
            DeduplicationStatus = $"Анализ не выполнен: {e.GetBaseException().Message}";
            _main.ShowToast("Не удалось проверить одинаковые файлы", true);
        }
        finally
        {
            IsDeduplicationRunning = false;
        }
    }

    public async void OptimizeDuplicateContent()
    {
        if (IsDeduplicationRunning) return;
        IsDeduplicationRunning = true;
        DeduplicationStatus = "Удаление сиротских данных и уплотнение базы…";
        try
        {
            using var measurement = PerformanceTelemetry.Measure("Оптимизация дедупликации контента");
            var result = await _contentManager.OptimizeDeduplication();
            DeduplicationStatus = result.Success
                ? $"{result.Details} Освобождено: {Helpers.FormatBytes(result.FreedBytes)}."
                : result.Details;
            _main.ShowToast(result.Success ? "Хранилище контента оптимизировано" : result.Details, !result.Success);
            RefreshServerContent();
            ScanDisk();
        }
        finally
        {
            IsDeduplicationRunning = false;
            AnalyzeDuplicateContent();
        }
    }

    public void RefreshServerContent()
    {
        using var measurement = PerformanceTelemetry.Measure("Чтение базы контента");
        ServerContent.Clear();
        foreach (var version in _contentManager.GetManagedVersions())
            ServerContent.Add(new ServerContentItem(this, version));
        OnPropertyChanged(nameof(ContentDatabaseSize));
        OnPropertyChanged(nameof(HasSelectedContent));
    }

    public async void DeleteSelectedContent()
    {
        var selectedItems = ServerContent.Where(x => x.IsSelected && !x.InUse).ToArray();
        var selected = selectedItems.Select(x => x.Id).ToArray();
        if (selected.Length == 0) { _main.ShowToast("Выберите хотя бы одну сборку"); return; }
        ShowCleanupPreview(selected, [], selectedItems.Sum(x => x.SizeBytes),
            $"Сборки серверов: {selected.Length:N0}");
        await Task.CompletedTask;
    }

    public async void DeleteAllContent()
    {
        var items = ServerContent.Where(x => !x.InUse).ToArray();
        if (items.Length == 0) { _main.ShowToast("Нет доступного для очистки контента"); return; }
        ShowCleanupPreview(items.Select(x => x.Id).ToArray(), [], items.Sum(x => x.SizeBytes),
            $"Все неиспользуемые сборки: {items.Length:N0}");
        await Task.CompletedTask;
    }

    public void SelectOldContent()
    {
        foreach (var item in ServerContent)
            item.IsSelected = item.IsOld && !item.InUse;
    }

    public async void AnalyzeLaunchLogs()
    {
        if (IsAnalyzingLogs) return;
        IsAnalyzingLogs = true;
        try
        {
            using var measurement = PerformanceTelemetry.Measure("Анализ журналов запуска");
            var results = await Task.Run(AnalyzeLaunchLogsCore);
            LaunchDiagnostics.Clear();
            foreach (var result in results)
                LaunchDiagnostics.Add(result);
        }
        catch (Exception e)
        {
            LaunchDiagnostics.Clear();
            LaunchDiagnostics.Add(LaunchDiagnosticItem.Warning("Не удалось прочитать журналы",
                e.GetBaseException().Message, "Проверьте права доступа к папке журналов."));
        }
        finally
        {
            IsAnalyzingLogs = false;
        }
    }

    public async void ScanDisk()
    {
        if (IsScanningDisk) return;
        IsScanningDisk = true;
        try
        {
            using var measurement = PerformanceTelemetry.Measure("Анализ дискового пространства");
            var items = await Task.Run(CollectDiskUsage);
            DiskUsage.Clear();
            foreach (var item in items)
                DiskUsage.Add(item);
            RefreshPerformance();
        }
        catch (Exception e)
        {
            _main.ShowToast($"Не удалось проанализировать диск: {e.GetBaseException().Message}", true);
        }
        finally
        {
            IsScanningDisk = false;
        }
    }

    public void PreviewDiskCleanup()
    {
        var selected = DiskUsage.Where(x => x.IsSelected && x.CanClean).ToArray();
        if (selected.Length == 0)
        {
            _main.ShowToast("Выберите кэш или старые журналы для очистки");
            return;
        }

        ShowCleanupPreview([], selected.Select(x => x.Kind).ToArray(), selected.Sum(x => x.CleanupBytes),
            string.Join("\n", selected.Select(x => $"• {x.Title}: {x.CleanupSize}")));
    }

    public void CancelCleanup()
    {
        _pendingContentIds = [];
        _pendingDiskKinds = [];
        IsCleanupPreviewVisible = false;
        CleanupPreviewText = string.Empty;
    }

    public async void ConfirmCleanup()
    {
        var contentIds = _pendingContentIds;
        var diskKinds = _pendingDiskKinds;
        CancelCleanup();

        try
        {
            if (contentIds.Length > 0 && !await _contentManager.RemoveVersions(contentIds))
            {
                _main.ShowToast("Часть контента используется запущенным клиентом", true);
                return;
            }

            await Task.Run(() => CleanupDiskKinds(diskKinds));
            _main.ShowToast("Безопасная очистка завершена");
            RefreshServerContent();
            ScanDisk();
        }
        catch (Exception e)
        {
            _main.ShowToast($"Очистка не завершена: {e.GetBaseException().Message}", true);
        }
    }

    public void RefreshPerformance()
    {
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var now = DateTime.UtcNow;
        var cpuTime = process.TotalProcessorTime;
        var elapsed = Math.Max(0.001, (now - _lastCpuSample).TotalSeconds);
        var cpu = Math.Clamp((cpuTime - _lastCpuTime).TotalSeconds / elapsed / Environment.ProcessorCount * 100, 0, 100);
        _lastCpuSample = now;
        _lastCpuTime = cpuTime;

        var serverLoad = PerformanceTelemetry.Latest("Загрузка списка серверов");
        PerformanceMetrics.Clear();
        PerformanceMetrics.Add(new("Запуск интерфейса", PerformanceTelemetry.UiReady is { } ready ? FormatDuration(ready) : "Измеряется", "От старта процесса до окна"));
        PerformanceMetrics.Add(new("Память процесса", Helpers.FormatBytes(process.WorkingSet64), "Фактическая рабочая память"));
        PerformanceMetrics.Add(new("Управляемая память", Helpers.FormatBytes(GC.GetTotalMemory(false)), "Объекты .NET"));
        PerformanceMetrics.Add(new("Загрузка CPU", $"{cpu:F1}%", $"{Environment.ProcessorCount} логических потоков"));
        PerformanceMetrics.Add(new("Список серверов", serverLoad == null ? "Нет данных" : FormatDuration(serverLoad.Elapsed), serverLoad?.Details ?? "Обновите список серверов"));
        PerformanceMetrics.Add(new("Время работы", FormatDuration(PerformanceTelemetry.Uptime), "Текущий сеанс Orbitra"));

        SlowOperations.Clear();
        foreach (var operation in PerformanceTelemetry.Snapshot()
                     .OrderByDescending(x => x.Elapsed)
                     .Take(12))
            SlowOperations.Add(new PerformanceOperationItem(operation));
    }

    internal void ContentSelectionChanged() => OnPropertyChanged(nameof(HasSelectedContent));

    public async void RunIntegrity()
    {
        using var measurement = PerformanceTelemetry.Measure("Проверка целостности");
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
        using var measurement = PerformanceTelemetry.Measure("Проверка служб");
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
        using var measurement = PerformanceTelemetry.Measure("Загрузка истории обновлений");
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
            WriteEntry(zip, "launch-diagnostics.txt", string.Join("\n\n", LaunchDiagnostics.Select(x =>
                $"[{x.Severity}] {x.Title}\n{x.Details}\n{x.Evidence}\nРекомендация: {x.Recommendation}")));
            WriteEntry(zip, "disk-usage.txt", string.Join('\n', DiskUsage.Select(x =>
                $"{x.Title}: {x.Size} · {x.Path}")));
            WriteEntry(zip, "performance.txt", string.Join('\n', PerformanceTelemetry.Snapshot().Select(x =>
                $"{x.Timestamp:O} · {x.Name}: {x.Elapsed.TotalMilliseconds:F0} ms · {x.Details}")));
            _main.ShowToast("Обезличенный отчёт создан");
        }
        catch (Exception e) { _main.ShowToast($"Не удалось создать отчёт: {e.Message}", true); }
    }

    private List<LaunchDiagnosticItem> AnalyzeLaunchLogsCore()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            LauncherPaths.PathClientStdoutLog,
            LauncherPaths.PathClientStderrLog,
            LauncherPaths.PathClientMacLog,
            Path.Combine(LauncherPaths.DirLogs, "last-crash.txt")
        };

        if (Directory.Exists(LauncherPaths.DirLogs))
        {
            foreach (var file in Directory.EnumerateFiles(LauncherPaths.DirLogs)
                         .OrderByDescending(File.GetLastWriteTimeUtc).Take(8))
                candidates.Add(file);
        }

        var sources = candidates.Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path => new LogSource(path, ReadLogTail(path)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .ToArray();

        if (sources.Length == 0)
        {
            return [LaunchDiagnosticItem.Info("Журналы запуска пока отсутствуют",
                "Запустите клиент хотя бы один раз, затем повторите анализ.",
                "Orbitra проверит последние журналы клиента и лаунчера.")];
        }

        var rules = new[]
        {
            new DiagnosticRule("content", "Повреждённый контент", "Ошибка",
                @"(?i)(hash|checksum).{0,40}(mismatch|invalid|failed)|content.{0,40}(corrupt|damaged)|database disk image is malformed|sqlite.{0,30}corrupt|manifest.{0,30}(invalid|failed)|failed to (load|read).{0,30}(resource|content)",
                "Удалите контент этого сервера в разделе «Диск» и подключитесь снова."),
            new DiagnosticRule("loader", "Loader или runtime не запускается", "Ошибка",
                @"(?i)(SS14\.Loader|loader).{0,50}(missing|not found|failed|exception)|hostfxr|hostpolicy|framework.{0,30}(not found|missing)|failed to start.{0,30}(loader|runtime)",
                "Запустите проверку целостности. Если Loader отсутствует, переустановите Orbitra поверх текущей версии."),
            new DiagnosticRule("auth", "Ошибка авторизации", "Ошибка",
                @"(?i)\b(401|403)\b|unauthorized|forbidden|authentication.{0,40}(fail|error|denied)|token.{0,30}(expired|invalid)|login.{0,30}(fail|error)",
                "Перезайдите в аккаунт SS14 и проверьте системное время Windows."),
            new DiagnosticRule("memory", "Недостаточно памяти", "Ошибка",
                @"(?i)OutOfMemoryException|out of memory|not enough memory|insufficient memory|paging file.{0,30}(small|too small)|0x8007000E",
                "Закройте тяжёлые приложения, освободите память и убедитесь, что файл подкачки Windows включён."),
            new DiagnosticRule("server", "Сервер или сеть недоступны", "Предупреждение",
                @"(?i)connection (refused|reset)|timed? out|timeout|no such host|name or service not known|handshake.{0,30}(fail|error)|server.{0,30}unavailable|\bHTTP.{0,8}5\d\d\b",
                "Проверьте пинг сервера. Если другие серверы работают, проблема, вероятно, на стороне выбранного сервера."),
            new DiagnosticRule("graphics", "Ошибка графического устройства", "Предупреждение",
                @"(?i)(vulkan|direct3d|d3d|gpu|graphics device).{0,50}(lost|unsupported|failed|error)|device removed|VK_ERROR",
                "Обновите драйвер видеокарты и попробуйте другой графический режим клиента.")
        };

        var results = new List<LaunchDiagnosticItem>();
        foreach (var rule in rules)
        {
            foreach (var source in sources)
            {
                var match = Regex.Match(source.Content, rule.Pattern, RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                var line = GetEvidenceLine(source.Content, match.Index);
                results.Add(new LaunchDiagnosticItem(rule.Title,
                    $"Найдено в {Path.GetFileName(source.Path)} · {File.GetLastWriteTime(source.Path):dd.MM HH:mm}",
                    rule.Recommendation, Redact(line), rule.Severity));
                break;
            }
        }

        if (results.Count == 0)
        {
            results.Add(LaunchDiagnosticItem.Success("Критических причин не найдено",
                $"Проверено журналов: {sources.Length}",
                "Если сбой повторится, сразу запустите анализ ещё раз или экспортируйте обезличенный отчёт."));
        }

        return results;
    }

    private List<DiskUsageItem> CollectDiskUsage()
    {
        var contentBytes = _contentManager.GetDatabaseSize();
        var iconBytes = GetDirectorySize(_iconCachePath);
        var updateBytes = GetDirectorySize(_updatesPath);
        var logsBytes = GetDirectorySize(LauncherPaths.DirLogs);
        var oldLogsBytes = GetOldLogSize();
        var localServersBytes = GetDirectorySize(_localServersPath);
        var themeBytes = GetDirectorySize(_themesPath);

        return
        [
            new("Контент серверов", "Сборки управляются отдельно в таблице ниже", contentBytes, 0,
                string.Empty, DiskCleanupKind.None, false),
            new("Кэш изображений", "Иконки серверов будут загружены повторно", iconBytes, iconBytes,
                _iconCachePath, DiskCleanupKind.ImageCache, true),
            new("Загруженные обновления", "Временные пакеты установщика и обновлений", updateBytes, updateBytes,
                _updatesPath, DiskCleanupKind.Updates, true),
            new("Журналы", $"Для очистки доступны журналы старше 7 дней: {Helpers.FormatBytes(oldLogsBytes)}",
                logsBytes, oldLogsBytes, LauncherPaths.DirLogs, DiskCleanupKind.OldLogs, oldLogsBytes > 0),
            new("Локальные серверы", "Защищено: удаляется только вручную во вкладке локальных серверов",
                localServersBytes, 0, _localServersPath, DiskCleanupKind.None, false),
            new("Темы и фоны", "Защищено: пользовательские данные не очищаются автоматически",
                themeBytes, 0, _themesPath, DiskCleanupKind.None, false)
        ];
    }

    private void ShowCleanupPreview(long[] contentIds, DiskCleanupKind[] diskKinds, long bytes, string details)
    {
        _pendingContentIds = contentIds;
        _pendingDiskKinds = diskKinds;
        CleanupPreviewText = $"Будет освобождено примерно {Helpers.FormatBytes(bytes)}.\n{details}\n\nОперация затронет только перечисленные данные.";
        IsCleanupPreviewVisible = true;
    }

    private void CleanupDiskKinds(IEnumerable<DiskCleanupKind> kinds)
    {
        foreach (var kind in kinds.Distinct())
        {
            switch (kind)
            {
                case DiskCleanupKind.ImageCache:
                    ClearKnownDirectory(_iconCachePath, Path.Combine(LauncherPaths.DirLocalData, "server-icons"));
                    break;
                case DiskCleanupKind.Updates:
                    ClearKnownDirectory(_updatesPath, Path.Combine(LauncherPaths.DirLocalData, "updates"));
                    break;
                case DiskCleanupKind.OldLogs:
                    if (!Directory.Exists(LauncherPaths.DirLogs)) break;
                    foreach (var file in Directory.EnumerateFiles(LauncherPaths.DirLogs))
                    {
                        if (File.GetLastWriteTimeUtc(file) >= DateTime.UtcNow.AddDays(-7)) continue;
                        try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    }
                    break;
            }
        }
    }

    private static void ClearKnownDirectory(string path, string expectedPath)
    {
        var resolved = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var expected = Path.GetFullPath(expectedPath).TrimEnd(Path.DirectorySeparatorChar);
        if (!resolved.Equals(expected, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(resolved))
            return;

        foreach (var file in Directory.EnumerateFiles(resolved))
            File.Delete(file);
        foreach (var directory in Directory.EnumerateDirectories(resolved))
            Directory.Delete(directory, true);
    }

    private long GetOldLogSize()
    {
        if (!Directory.Exists(LauncherPaths.DirLogs)) return 0;
        return Directory.EnumerateFiles(LauncherPaths.DirLogs)
            .Where(x => File.GetLastWriteTimeUtc(x) < DateTime.UtcNow.AddDays(-7))
            .Sum(GetFileSizeSafe);
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(GetFileSizeSafe);
        }
        catch (UnauthorizedAccessException) { return 0; }
        catch (IOException) { return 0; }
    }

    private static long GetFileSizeSafe(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    private static string ReadLogTail(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int maxBytes = 2 * 1024 * 1024;
            if (stream.Length > maxBytes)
                stream.Seek(-maxBytes, SeekOrigin.End);
            using var reader = new StreamReader(stream, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }
        catch { return string.Empty; }
    }

    private static string GetEvidenceLine(string content, int index)
    {
        var start = content.LastIndexOf('\n', Math.Max(0, index - 1));
        var end = content.IndexOf('\n', index);
        start = start < 0 ? 0 : start + 1;
        end = end < 0 ? content.Length : end;
        var line = content[start..end].Trim();
        return line.Length <= 260 ? line : line[..257] + "…";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1) return $"{duration.TotalMinutes:F1} мин";
        if (duration.TotalSeconds >= 1) return $"{duration.TotalSeconds:F2} с";
        return $"{duration.TotalMilliseconds:F0} мс";
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
    public string Files { get; } public bool InUse { get; } public long SizeBytes { get; }
    public bool IsOld { get; }
    public string AgeStatus => IsOld ? "ДАВНО НЕ ИСПОЛЬЗОВАЛОСЬ" : string.Empty;
    public bool IsSelected { get => _isSelected; set { if (SetProperty(ref _isSelected, value)) _owner.ContentSelectionChanged(); } }
    public ServerContentItem(SystemCenterTabViewModel owner, ManagedContentVersion item)
    {
        _owner = owner; Id = item.Id; ForkId = item.ForkId; ForkVersion = item.ForkVersion;
        Engine = item.EngineVersion; LastUsed = item.LastUsed.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        SizeBytes = item.LogicalSize; Size = Helpers.FormatBytes(item.LogicalSize);
        Files = $"{item.FileCount:N0} файлов"; InUse = item.InUse;
        IsOld = !item.InUse && item.LastUsed < DateTimeOffset.Now.AddDays(-30);
    }
}

public enum DiskCleanupKind
{
    None,
    ImageCache,
    Updates,
    OldLogs
}

public sealed class DiskUsageItem : ObservableObject
{
    private bool _isSelected;
    public string Title { get; }
    public string Details { get; }
    public long SizeBytes { get; }
    public long CleanupBytes { get; }
    public string Size => Helpers.FormatBytes(SizeBytes);
    public string CleanupSize => Helpers.FormatBytes(CleanupBytes);
    public string Path { get; }
    public DiskCleanupKind Kind { get; }
    public bool CanClean { get; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public DiskUsageItem(string title, string details, long sizeBytes, long cleanupBytes,
        string path, DiskCleanupKind kind, bool canClean)
    {
        Title = title;
        Details = details;
        SizeBytes = sizeBytes;
        CleanupBytes = cleanupBytes;
        Path = path;
        Kind = kind;
        CanClean = canClean;
    }
}

public sealed record LaunchDiagnosticItem(
    string Title,
    string Details,
    string Recommendation,
    string Evidence,
    string Severity)
{
    public string Marker => Severity == "Ошибка" ? "×" : Severity == "Предупреждение" ? "!" : "✓";
    public string Color => Severity == "Ошибка" ? "#D76464" : Severity == "Предупреждение" ? "#D6A84B" : "#63C174";
    public bool HasEvidence => !string.IsNullOrWhiteSpace(Evidence);

    public static LaunchDiagnosticItem Warning(string title, string details, string recommendation) =>
        new(title, details, recommendation, string.Empty, "Предупреждение");
    public static LaunchDiagnosticItem Info(string title, string details, string recommendation) =>
        new(title, details, recommendation, string.Empty, "Информация");
    public static LaunchDiagnosticItem Success(string title, string details, string recommendation) =>
        new(title, details, recommendation, string.Empty, "Готово");
}

public sealed record PerformanceMetricItem(string Title, string Value, string Details);

public sealed class PerformanceOperationItem
{
    public string Name { get; }
    public string Duration { get; }
    public string Details { get; }
    public string Timestamp { get; }

    public PerformanceOperationItem(PerformanceOperation operation)
    {
        Name = operation.Name;
        Duration = operation.Elapsed.TotalSeconds >= 1
            ? $"{operation.Elapsed.TotalSeconds:F2} с"
            : $"{operation.Elapsed.TotalMilliseconds:F0} мс";
        Details = operation.Details;
        Timestamp = operation.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    }
}

file sealed record LogSource(string Path, string Content);
file sealed record DiagnosticRule(string Id, string Title, string Severity, string Pattern, string Recommendation);
