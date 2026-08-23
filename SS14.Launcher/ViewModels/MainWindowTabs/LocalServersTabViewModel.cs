using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed partial class LocalServersTabViewModel : MainWindowTabViewModel
{
    private const long MaximumExtractedBytes = 8L * 1024 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly MainWindowViewModel _main;
    private readonly string _root = Path.Combine(LauncherPaths.DirUserData, "LocalServers");
    private readonly string _profilesPath;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(30) };

    public override string Name => "Локальные серверы";
    // Official Lucide "square-terminal" icon.
    public override string IconData => "M5,3 H19 A2,2 0 0 1 21,5 V19 A2,2 0 0 1 19,21 H5 A2,2 0 0 1 3,19 V5 A2,2 0 0 1 5,3 Z M7,8 L11,12 L7,16 M13,16 H17";

    public ObservableCollection<LocalServerProfileViewModel> Servers { get; } = [];
    public ObservableCollection<LocalServerProfileViewModel> VisibleServers { get; } = [];
    public ObservableCollection<string> ConsoleLines { get; } = [];

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            OnPropertyChanged(nameof(HasSearchText));
            RefreshVisibleServers();
        }
    }
    public bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);

    private LocalServerProfileViewModel? _selectedServer;
    public LocalServerProfileViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (!SetProperty(ref _selectedServer, value)) return;
            ConsoleLines.Clear();
            if (value != null) foreach (var line in value.ConsoleHistory) ConsoleLines.Add(line);
            NotifyState();
        }
    }
    private string _sourceUrl = "https://cdn.ss14.org/fork/fish_station";
    public string SourceUrl
    {
        get => _sourceUrl;
        set { if (SetProperty(ref _sourceUrl, value)) AddUrlCommand.NotifyCanExecuteChanged(); }
    }
    [ObservableProperty] private string _statusText = "Добавьте ZIP или адрес совместимого CDN.";
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { if (SetProperty(ref _isBusy, value)) NotifyState(); }
    }
    [ObservableProperty] private double _progress;
    private bool _isAddPanelOpen;
    public bool IsAddPanelOpen { get => _isAddPanelOpen; set => SetProperty(ref _isAddPanelOpen, value); }
    private string _consoleCommand = string.Empty;
    public string ConsoleCommand
    {
        get => _consoleCommand;
        set { if (SetProperty(ref _consoleCommand, value)) SendCommand.NotifyCanExecuteChanged(); }
    }

    public bool HasServers => Servers.Count > 0;
    public bool HasVisibleServers => VisibleServers.Count > 0;
    public bool HasSelection => SelectedServer != null;
    public bool CanStart => SelectedServer is { IsRunning: false } && !IsBusy;
    public bool CanStop => SelectedServer?.IsRunning == true;
    public bool CanRestart => CanStop;
    public bool CanConnect => CanStop;
    public string ServerCountText => $"{Servers.Count:N0} всего";
    public string RunningCountText => $"{Servers.Count(x => x.IsRunning):N0} запущено";
    public string ManagedCountText => $"{Servers.Count(x => x.ManagedFiles):N0} управляемых";
    public string ConsoleLineCountText => $"{ConsoleLines.Count:N0} строк";

    public IAsyncRelayCommand ImportZipCommand { get; }
    public IAsyncRelayCommand ImportFolderCommand { get; }
    public IAsyncRelayCommand AddUrlCommand { get; }
    public IRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand SendCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }
    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand LoadConfigCommand { get; }
    public IRelayCommand SaveConfigCommand { get; }
    public IRelayCommand OpenAddPanelCommand { get; }
    public IRelayCommand CloseAddPanelCommand { get; }
    public IRelayCommand RestartCommand { get; }
    public IRelayCommand ConnectCommand { get; }
    public IRelayCommand ClearConsoleCommand { get; }
    public IRelayCommand ClearSearchCommand { get; }
    private readonly DispatcherTimer _metricsTimer;

    public LocalServersTabViewModel(MainWindowViewModel main)
    {
        _main = main;
        _profilesPath = Path.Combine(_root, "profiles.json");
        Directory.CreateDirectory(_root);
        ImportZipCommand = new AsyncRelayCommand(ImportZipAsync, () => !IsBusy);
        ImportFolderCommand = new AsyncRelayCommand(ImportFolderAsync, () => !IsBusy);
        AddUrlCommand = new AsyncRelayCommand(AddUrlAsync, () => !IsBusy && Uri.TryCreate(SourceUrl, UriKind.Absolute, out _));
        StartCommand = new RelayCommand(StartSelected, () => CanStart);
        StopCommand = new RelayCommand(StopSelected, () => CanStop);
        SendCommand = new RelayCommand(SendConsoleCommand, () => CanStop && !string.IsNullOrWhiteSpace(ConsoleCommand));
        SaveCommand = new RelayCommand(SaveProfiles, () => HasSelection);
        OpenFolderCommand = new RelayCommand(OpenSelectedFolder, () => HasSelection);
        RemoveCommand = new RelayCommand(RemoveSelected, () => HasSelection && !CanStop);
        LoadConfigCommand = new RelayCommand(LoadSelectedConfig, () => HasSelection);
        SaveConfigCommand = new RelayCommand(SaveSelectedConfig, () => HasSelection && !CanStop);
        OpenAddPanelCommand = new RelayCommand(() => IsAddPanelOpen = true);
        CloseAddPanelCommand = new RelayCommand(() => IsAddPanelOpen = false);
        RestartCommand = new RelayCommand(RestartSelected, () => CanRestart);
        ConnectCommand = new RelayCommand(ConnectSelected, () => CanConnect);
        ClearConsoleCommand = new RelayCommand(ClearConsole, () => HasSelection);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        ConsoleLines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ConsoleLineCountText));
        LoadProfiles();
        RefreshVisibleServers();
        _metricsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _metricsTimer.Tick += (_, _) => { foreach (var server in Servers) server.RefreshRuntimeStats(); };
        _metricsTimer.Start();
    }

    private async Task ImportZipAsync()
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите ZIP серверной сборки",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("ZIP-сборка") { Patterns = ["*.zip"] }]
        });
        if (files.Count == 0) return;
        await ImportArchiveAsync(files[0].Path.LocalPath, null, null, Path.GetFileNameWithoutExtension(files[0].Name));
    }

    private async Task ImportFolderAsync()
    {
        if (_main.Control?.StorageProvider is not { } storage) return;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = "Выберите папку серверной сборки", AllowMultiple = false });
        if (folders.Count == 0) return;
        var directory = Path.GetFullPath(folders[0].Path.LocalPath);
        try
        {
            var candidates = FindLaunchFiles(directory).ToList();
            if (candidates.Count == 0) throw new InvalidDataException("В папке не найден .bat, .cmd или Robust.Server.exe.");
            var configs = FindConfigFiles(directory).ToList();
            var profile = new LocalServerProfileViewModel
            {
                Id = Guid.NewGuid().ToString("N"), Name = CleanSuggestedName(Path.GetFileName(directory)), Directory = directory,
                LaunchFile = candidates.FirstOrDefault(x => x.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)) ?? candidates.First(),
                LaunchFiles = new ObservableCollection<string>(candidates), ConfigFiles = new ObservableCollection<string>(configs),
                ConfigFile = configs.FirstOrDefault() ?? string.Empty, Version = "Готовая папка", ManagedFiles = false
            };
            profile.PropertyChanged += ProfileChanged;
            Servers.Add(profile); SelectedServer = profile; IsAddPanelOpen = false; SaveProfiles();
            RefreshVisibleServers(); StatusText = "Папка добавлена без копирования файлов.";
        }
        catch (Exception e) { StatusText = "Ошибка добавления папки: " + e.Message; _main.ShowToast(StatusText, true); }
    }

    private async Task AddUrlAsync()
    {
        IsBusy = true;
        Progress = 0;
        try
        {
            var input = SourceUrl.Trim().TrimEnd('/');
            if (input.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await DownloadAndImportAsync(input, null, new Uri(input).Host);
                return;
            }

            var manifestUrl = input.EndsWith("/manifest", StringComparison.OrdinalIgnoreCase)
                ? input
                : input + "/manifest";
            StatusText = "Чтение CDN manifest…";
            using var stream = await _http.GetStreamAsync(manifestUrl);
            using var document = await JsonDocument.ParseAsync(stream);
            var builds = document.RootElement.GetProperty("builds");
            var latest = builds.EnumerateObject()
                .Select(build => new { build.Name, Data = build.Value, Time = build.Value.GetProperty("time").GetDateTimeOffset() })
                .OrderByDescending(x => x.Time)
                .First();
            var package = latest.Data.GetProperty("server").GetProperty("win-x64");
            var url = package.GetProperty("url").GetString() ?? throw new InvalidDataException("CDN не содержит URL win-x64.");
            var hash = package.TryGetProperty("sha256", out var hashNode) ? hashNode.GetString() : null;
            var channelName = new Uri(manifestUrl).Segments.Reverse().Skip(1).FirstOrDefault()?.Trim('/') ?? "CDN server";
            await DownloadAndImportAsync(url, hash, channelName, manifestUrl, latest.Name);
        }
        catch (Exception e)
        {
            StatusText = "Ошибка CDN: " + e.Message;
            _main.ShowToast(StatusText, true);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    private async Task DownloadAndImportAsync(string url, string? sha256, string name, string? manifestUrl = null, string? version = null)
    {
        var temp = Path.Combine(Path.GetTempPath(), "orbitra-local-server-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            StatusText = "Загрузка серверной сборки…";
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read));
                    received += read;
                    if (total > 0) Progress = (double)received / total.Value;
                }
            }
            if (!string.IsNullOrWhiteSpace(sha256))
            {
                StatusText = "Проверка SHA-256…";
                await using var file = File.OpenRead(temp);
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
                if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("SHA-256 скачанного архива не совпадает с manifest.");
            }
            await ImportArchiveAsync(temp, manifestUrl, version, name);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private async Task ImportArchiveAsync(string archivePath, string? manifestUrl, string? version, string suggestedName)
    {
        IsBusy = true;
        try
        {
            StatusText = "Проверка архива…";
            var id = Guid.NewGuid().ToString("N");
            var directory = Path.Combine(_root, id);
            Directory.CreateDirectory(directory);
            try
            {
                await Task.Run(() => ExtractSafely(archivePath, directory));
                var candidates = FindLaunchFiles(directory).ToList();
                if (candidates.Count == 0)
                    throw new InvalidDataException("В архиве не найден .bat, .cmd или Robust.Server.exe.");
                var preferred = candidates.FirstOrDefault(x => x.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                                ?? candidates.First();
                var configFiles = FindConfigFiles(directory).ToList();
                var profile = new LocalServerProfileViewModel
                {
                    Id = id,
                    Name = CleanSuggestedName(suggestedName),
                    Directory = directory,
                    LaunchFile = preferred,
                    LaunchFiles = new ObservableCollection<string>(candidates),
                    ConfigFiles = new ObservableCollection<string>(configFiles),
                    ConfigFile = configFiles.FirstOrDefault() ?? string.Empty,
                    SourceManifest = manifestUrl ?? string.Empty,
                    Version = version ?? "Локальный ZIP",
                    ManagedFiles = true
                };
                profile.PropertyChanged += ProfileChanged;
                Servers.Add(profile);
                SelectedServer = profile;
                SaveProfiles();
                RefreshVisibleServers();
                StatusText = $"Сервер «{profile.Name}» готов к настройке и запуску.";
                IsAddPanelOpen = false;
                _main.ShowToast("Локальный сервер добавлен");
            }
            catch
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                throw;
            }
        }
        catch (Exception e)
        {
            StatusText = "Ошибка импорта: " + e.Message;
            _main.ShowToast(StatusText, true);
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    private static void ExtractSafely(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        long expanded = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            expanded += entry.Length;
            if (expanded > MaximumExtractedBytes) throw new InvalidDataException("Распакованный архив превышает лимит 8 ГБ.");
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Архив содержит небезопасный путь.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static IEnumerable<string> FindLaunchFiles(string root)
    {
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Equals(".orbitra-launch.cmd", StringComparison.OrdinalIgnoreCase))
            .Where(path => path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
                           || Path.GetFileName(path).Equals("Robust.Server.exe", StringComparison.OrdinalIgnoreCase));
        return files.Select(path => Path.GetRelativePath(root, path)).OrderBy(path => path.Count(c => c is '/' or '\\')).ThenBy(path => path);
    }

    private static string CleanSuggestedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Локальный сервер";
        name = name.Replace('_', ' ').Trim();
        name = System.Text.RegularExpressions.Regex.Replace(name, @"(?i)[\s_-]*win[- ]?(x64|arm64)(\s*\(\d+\))?$", string.Empty);
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*\(\d+\)$", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Локальный сервер" : name;
    }

    private static IEnumerable<string> FindConfigFiles(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)
                       || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                       || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                       || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        .Where(path => new FileInfo(path).Length <= 2 * 1024 * 1024)
        .Select(path => Path.GetRelativePath(root, path))
        .OrderBy(path => path.Contains("server", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ThenBy(path => path);

    private void StartSelected()
    {
        var server = SelectedServer;
        if (server == null || server.IsRunning) return;
        var launchPath = Path.GetFullPath(Path.Combine(server.Directory, server.LaunchFile));
        if (!launchPath.StartsWith(Path.GetFullPath(server.Directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(launchPath))
        {
            StatusText = "Файл запуска не найден или находится вне папки сервера.";
            return;
        }
        try
        {
            var info = new ProcessStartInfo
            {
                WorkingDirectory = Path.GetDirectoryName(launchPath)!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (launchPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || launchPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
            {
                // Passing a complete CALL expression through ArgumentList makes cmd.exe quote it a second time.
                // A tiny wrapper keeps paths with spaces reliable and switches cmd output to UTF-8.
                var wrapper = Path.Combine(Path.GetDirectoryName(launchPath)!, ".orbitra-launch.cmd");
                var safeArguments = server.Arguments.Replace('\r', ' ').Replace('\n', ' ');
                var wrapperText = "@echo off\r\nchcp 65001 >nul\r\ncall \"" + Path.GetFileName(launchPath) + "\" " + safeArguments + "\r\nexit /b %errorlevel%\r\n";
                File.WriteAllText(wrapper, wrapperText, new UTF8Encoding(false));
                info.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
                info.ArgumentList.Add("/D"); info.ArgumentList.Add("/Q"); info.ArgumentList.Add("/C");
                info.ArgumentList.Add(wrapper);
            }
            else
            {
                info.FileName = launchPath;
                if (!string.IsNullOrWhiteSpace(server.Arguments)) info.Arguments = server.Arguments;
            }
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => AppendConsole(server, e.Data);
            process.ErrorDataReceived += (_, e) => AppendConsole(server, e.Data == null ? null : "[ERR] " + e.Data);
            process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                server.IsRunning = false;
                server.Process = null;
                AppendConsole(server, $"[Orbitra] Процесс завершён с кодом {process.ExitCode}.");
                NotifyState();
            });
            if (!process.Start()) throw new InvalidOperationException("Не удалось создать процесс.");
            server.Process = process;
            server.IsRunning = true;
            server.LastStartedUtc = DateTimeOffset.UtcNow;
            server.NotifyLastStartedChanged();
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            AppendConsole(server, $"[Orbitra] Запущен {server.LaunchFile}, PID {process.Id}.");
            SaveProfiles();
            StatusText = $"Сервер «{server.Name}» запущен.";
            NotifyState();
        }
        catch (Exception e)
        {
            StatusText = "Ошибка запуска: " + e.Message;
            AppendConsole(server, "[Orbitra] " + StatusText);
            _main.ShowToast(StatusText, true);
        }
    }

    private void StopSelected()
    {
        var server = SelectedServer;
        if (server?.Process is not { HasExited: false } process) return;
        try { process.Kill(true); StatusText = "Остановка сервера…"; }
        catch (Exception e) { StatusText = "Не удалось остановить сервер: " + e.Message; }
    }

    private async void RestartSelected()
    {
        if (SelectedServer?.Process is not { HasExited: false } process) return;
        try
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            await Task.Delay(350);
            StartSelected();
        }
        catch (Exception e) { StatusText = "Перезапуск не выполнен: " + e.Message; }
    }

    private void ConnectSelected()
    {
        if (SelectedServer is not { IsRunning: true } server) return;
        ConnectingViewModel.StartConnect(_main, $"ss14://127.0.0.1:{server.Port}");
    }

    private void ClearConsole()
    {
        if (SelectedServer == null) return;
        SelectedServer.ConsoleHistory.Clear();
        ConsoleLines.Clear();
    }

    private async void SendConsoleCommand()
    {
        var server = SelectedServer;
        var command = ConsoleCommand.Trim();
        if (server?.Process is not { HasExited: false } process || command.Length == 0) return;
        try
        {
            await process.StandardInput.WriteLineAsync(command);
            await process.StandardInput.FlushAsync();
            AppendConsole(server, "> " + command);
            ConsoleCommand = string.Empty;
        }
        catch (Exception e) { StatusText = "Команда не отправлена: " + e.Message; }
    }

    private void AppendConsole(LocalServerProfileViewModel server, string? line)
    {
        if (line == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            server.ConsoleHistory.Add(line);
            while (server.ConsoleHistory.Count > 2000) server.ConsoleHistory.RemoveAt(0);
            if (ReferenceEquals(server, SelectedServer))
            {
                ConsoleLines.Add(line);
                while (ConsoleLines.Count > 2000) ConsoleLines.RemoveAt(0);
            }
        });
    }

    private void OpenSelectedFolder()
    {
        if (SelectedServer == null) return;
        Process.Start(new ProcessStartInfo { FileName = SelectedServer.Directory, UseShellExecute = true });
    }

    private void LoadSelectedConfig()
    {
        var server = SelectedServer;
        if (server == null || string.IsNullOrWhiteSpace(server.ConfigFile)) return;
        try
        {
            var path = ResolveInsideServer(server, server.ConfigFile);
            server.ConfigText = File.ReadAllText(path);
            StatusText = "Конфигурация загружена.";
        }
        catch (Exception e) { StatusText = "Не удалось открыть конфигурацию: " + e.Message; }
    }

    private void SaveSelectedConfig()
    {
        var server = SelectedServer;
        if (server == null || string.IsNullOrWhiteSpace(server.ConfigFile)) return;
        try
        {
            var path = ResolveInsideServer(server, server.ConfigFile);
            var backup = path + ".orbitra-backup";
            if (File.Exists(path)) File.Copy(path, backup, true);
            File.WriteAllText(path, server.ConfigText);
            SaveProfiles();
            StatusText = "Конфигурация сохранена, резервная копия создана.";
        }
        catch (Exception e) { StatusText = "Не удалось сохранить конфигурацию: " + e.Message; }
    }

    private static string ResolveInsideServer(LocalServerProfileViewModel server, string relative)
    {
        var root = Path.GetFullPath(server.Directory) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(server.Directory, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Путь выходит за пределы папки сервера.");
        return path;
    }

    private void RemoveSelected()
    {
        var server = SelectedServer;
        if (server == null || server.IsRunning) return;
        Servers.Remove(server);
        SelectedServer = Servers.FirstOrDefault();
        if (server.ManagedFiles) try { Directory.Delete(server.Directory, true); } catch { }
        SaveProfiles();
        RefreshVisibleServers();
    }

    private void LoadProfiles()
    {
        if (!File.Exists(_profilesPath)) return;
        try
        {
            var records = JsonSerializer.Deserialize<List<LocalServerRecord>>(File.ReadAllText(_profilesPath)) ?? [];
            foreach (var record in records.Where(x => Directory.Exists(x.Directory)))
            {
                var vm = LocalServerProfileViewModel.FromRecord(record);
                vm.LaunchFiles = new ObservableCollection<string>(FindLaunchFiles(vm.Directory));
                vm.ConfigFiles = new ObservableCollection<string>(FindConfigFiles(vm.Directory));
                vm.PropertyChanged += ProfileChanged;
                Servers.Add(vm);
            }
            SelectedServer = Servers.FirstOrDefault();
            RefreshVisibleServers();
        }
        catch (Exception e) { StatusText = "Не удалось прочитать профили: " + e.Message; }
    }

    private void SaveProfiles()
    {
        try
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(_profilesPath, JsonSerializer.Serialize(Servers.Select(x => x.ToRecord()).ToList(), JsonOptions));
            StatusText = SelectedServer == null ? StatusText : "Настройки сервера сохранены.";
        }
        catch (Exception e) { StatusText = "Не удалось сохранить настройки: " + e.Message; }
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasSelection)); OnPropertyChanged(nameof(CanStart)); OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanRestart)); OnPropertyChanged(nameof(CanConnect));
        ImportZipCommand.NotifyCanExecuteChanged(); AddUrlCommand.NotifyCanExecuteChanged(); StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged(); SendCommand.NotifyCanExecuteChanged(); SaveCommand.NotifyCanExecuteChanged();
        OpenFolderCommand.NotifyCanExecuteChanged(); RemoveCommand.NotifyCanExecuteChanged();
        LoadConfigCommand.NotifyCanExecuteChanged(); SaveConfigCommand.NotifyCanExecuteChanged();
        ImportFolderCommand.NotifyCanExecuteChanged(); RestartCommand.NotifyCanExecuteChanged(); ConnectCommand.NotifyCanExecuteChanged(); ClearConsoleCommand.NotifyCanExecuteChanged();
    }

    private void ProfileChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(LocalServerProfileViewModel.IsRunning))
        {
            NotifyState();
            OnPropertyChanged(nameof(RunningCountText));
            OnPropertyChanged(nameof(ManagedCountText));
        }
        if (args.PropertyName is nameof(LocalServerProfileViewModel.Name) or
            nameof(LocalServerProfileViewModel.Tags) or nameof(LocalServerProfileViewModel.Version))
            RefreshVisibleServers();
    }

    private void RefreshVisibleServers()
    {
        var query = SearchText.Trim();
        IEnumerable<LocalServerProfileViewModel> filtered = string.IsNullOrEmpty(query)
            ? Servers
            : Servers.Where(server => server.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      server.Tags.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      server.Version.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      server.SourceLabel.Contains(query, StringComparison.OrdinalIgnoreCase));
        VisibleServers.Clear();
        foreach (var server in filtered)
            VisibleServers.Add(server);
        OnPropertyChanged(nameof(HasServers));
        OnPropertyChanged(nameof(HasVisibleServers));
        OnPropertyChanged(nameof(ServerCountText));
        OnPropertyChanged(nameof(RunningCountText));
        OnPropertyChanged(nameof(ManagedCountText));
    }
}

public sealed partial class LocalServerProfileViewModel : ViewModelBase
{
    public string Id { get; set; } = string.Empty;
    [ObservableProperty] private string _name = "Локальный сервер";
    [ObservableProperty] private string _directory = string.Empty;
    [ObservableProperty] private string _launchFile = string.Empty;
    [ObservableProperty] private string _arguments = string.Empty;
    [ObservableProperty] private string _tags = string.Empty;
    [ObservableProperty] private string _sourceManifest = string.Empty;
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private DateTimeOffset? _lastStartedUtc;
    [ObservableProperty] private string _configFile = string.Empty;
    [ObservableProperty] private string _configText = string.Empty;
    [ObservableProperty] private int _port = 1212;
    [ObservableProperty] private bool _managedFiles = true;
    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        set { if (SetProperty(ref _isRunning, value)) OnPropertyChanged(nameof(Status)); }
    }
    public ObservableCollection<string> LaunchFiles { get; set; } = [];
    public ObservableCollection<string> ConfigFiles { get; set; } = [];
    public ObservableCollection<string> ConsoleHistory { get; } = [];
    internal Process? Process { get; set; }
    public string Status => IsRunning ? "РАБОТАЕТ" : "ОСТАНОВЛЕН";
    public string SourceLabel => !ManagedFiles ? "Готовая папка" : string.IsNullOrWhiteSpace(SourceManifest) ? "Локальный ZIP" : "CDN";
    public string ProcessIdText => Process is { HasExited: false } process ? process.Id.ToString() : "—";
    public string UptimeText => IsRunning && LastStartedUtc is { } started ? FormatDuration(DateTimeOffset.UtcNow - started) : "—";
    public string LastStartedText => LastStartedUtc is { } started
        ? started.ToLocalTime().ToString("dd.MM.yyyy · HH:mm")
        : "Ещё не запускался";
    internal void NotifyLastStartedChanged() => OnPropertyChanged(nameof(LastStartedText));
    public string MemoryText
    {
        get
        {
            try { if (Process is { HasExited: false } process) { process.Refresh(); return $"{process.WorkingSet64 / 1024d / 1024d:F0} МБ"; } }
            catch { }
            return "—";
        }
    }
    internal void RefreshRuntimeStats() { OnPropertyChanged(nameof(ProcessIdText)); OnPropertyChanged(nameof(UptimeText)); OnPropertyChanged(nameof(MemoryText)); }
    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? $"{(int)value.TotalHours}ч {value.Minutes:00}м" : $"{value.Minutes}м {value.Seconds:00}с";
    public LocalServerRecord ToRecord() => new(Id, Name, Directory, LaunchFile, Arguments, Tags, SourceManifest, Version, LastStartedUtc, ConfigFile, Port, ManagedFiles);
    public static LocalServerProfileViewModel FromRecord(LocalServerRecord x) => new()
    { Id=x.Id, Name=x.Name, Directory=x.Directory, LaunchFile=x.LaunchFile, Arguments=x.Arguments, Tags=x.Tags, SourceManifest=x.SourceManifest, Version=x.Version, LastStartedUtc=x.LastStartedUtc, ConfigFile=x.ConfigFile, Port=x.Port, ManagedFiles=x.ManagedFiles };
}

public sealed record LocalServerRecord(string Id, string Name, string Directory, string LaunchFile, string Arguments,
    string Tags, string SourceManifest, string Version, DateTimeOffset? LastStartedUtc, string ConfigFile = "", int Port = 1212, bool ManagedFiles = true);
