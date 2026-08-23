using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Mvvm.Input;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed partial class LocalServersTabViewModel
{
    private readonly string _snapshotsRoot = Path.Combine(LauncherPaths.DirUserData, "LocalServerSnapshots");
    private bool _healthCheckRunning;
    private readonly List<string> _commandHistory = [];
    private int _commandHistoryIndex;

    public ObservableCollection<LocalConsoleLineViewModel> FilteredConsoleEntries { get; } = [];
    public ObservableCollection<LocalSnapshotViewModel> Snapshots { get; } = [];

    private LocalServerProfileViewModel? _wizardProfile;
    public LocalServerProfileViewModel? WizardProfile { get => _wizardProfile; private set { if (SetProperty(ref _wizardProfile, value)) NotifyWizard(); } }
    private int _wizardStep;
    public int WizardStep { get => _wizardStep; private set { if (SetProperty(ref _wizardStep, value)) NotifyWizard(); } }
    private string _wizardStatus = "Выберите источник серверной сборки.";
    public string WizardStatus { get => _wizardStatus; private set => SetProperty(ref _wizardStatus, value); }
    public bool WizardSourceStep => WizardStep == 0;
    public bool WizardArchiveStep => WizardStep == 1;
    public bool WizardLaunchStep => WizardStep == 2;
    public bool WizardConfigStep => WizardStep == 3;
    public bool WizardIdentityStep => WizardStep == 4;
    public bool WizardValidationStep => WizardStep == 5;
    public bool WizardReadyStep => WizardStep == 6;
    public bool WizardCanGoBack => WizardStep > 0 && WizardStep < 6 && !IsBusy;
    public bool WizardCanGoNext => WizardProfile != null && WizardStep is >= 1 and < 6 && !IsBusy;
    public string WizardStepText => $"ШАГ {WizardStep + 1} ИЗ 7";
    public double WizardProgress => (WizardStep + 1) / 7d;

    private string _consoleSearch = string.Empty;
    public string ConsoleSearch { get => _consoleSearch; set { if (SetProperty(ref _consoleSearch, value)) RefreshAdvancedConsole(); } }
    private bool _showInfo = true;
    public bool ShowInfo { get => _showInfo; set { if (SetProperty(ref _showInfo, value)) RefreshAdvancedConsole(); } }
    private bool _showWarnings = true;
    public bool ShowWarnings { get => _showWarnings; set { if (SetProperty(ref _showWarnings, value)) RefreshAdvancedConsole(); } }
    private bool _showErrors = true;
    public bool ShowErrors { get => _showErrors; set { if (SetProperty(ref _showErrors, value)) RefreshAdvancedConsole(); } }
    private bool _showDebug = true;
    public bool ShowDebug { get => _showDebug; set { if (SetProperty(ref _showDebug, value)) RefreshAdvancedConsole(); } }
    private bool _showBookmarksOnly;
    public bool ShowBookmarksOnly { get => _showBookmarksOnly; set { if (SetProperty(ref _showBookmarksOnly, value)) RefreshAdvancedConsole(); } }
    [ObservableProperty] private bool _isConsoleAutoScrollPaused;
    private LocalConsoleLineViewModel? _selectedConsoleEntry;
    public LocalConsoleLineViewModel? SelectedConsoleEntry
    {
        get => _selectedConsoleEntry;
        set
        {
            if (!SetProperty(ref _selectedConsoleEntry, value)) return;
            CopyConsoleLineCommand?.NotifyCanExecuteChanged(); ToggleConsoleBookmarkCommand?.NotifyCanExecuteChanged();
        }
    }
    private int _consoleLineLimit = 3000;
    public int ConsoleLineLimit
    {
        get => _consoleLineLimit;
        set
        {
            var normalized = Math.Clamp(value, 250, 20000);
            if (!SetProperty(ref _consoleLineLimit, normalized)) return;
            TrimConsoleHistory();
        }
    }
    public int ConsoleErrorCount => SelectedServer?.AdvancedConsoleHistory.Count(x => x.Level == LocalConsoleLevel.Error) ?? 0;
    public int ConsoleWarningCount => SelectedServer?.AdvancedConsoleHistory.Count(x => x.Level == LocalConsoleLevel.Warning) ?? 0;

    private LocalSnapshotViewModel? _selectedSnapshot;
    public LocalSnapshotViewModel? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (!SetProperty(ref _selectedSnapshot, value)) return;
            RestoreSnapshotCommand?.NotifyCanExecuteChanged(); DeleteSnapshotCommand?.NotifyCanExecuteChanged();
        }
    }
    [ObservableProperty] private string _snapshotDescription = string.Empty;
    [ObservableProperty] private bool _snapshotFullBuild = true;
    [ObservableProperty] private bool _snapshotConfiguration = true;
    [ObservableProperty] private bool _snapshotDatabase = true;
    [ObservableProperty] private bool _snapshotSaves = true;
    [ObservableProperty] private bool _snapshotLogs;

    public IAsyncRelayCommand WizardNextCommand { get; private set; } = null!;
    public IAsyncRelayCommand WizardTestLaunchCommand { get; private set; } = null!;
    public IRelayCommand WizardBackCommand { get; private set; } = null!;
    public IRelayCommand WizardFinishCommand { get; private set; } = null!;
    public IAsyncRelayCommand CreateSnapshotCommand { get; private set; } = null!;
    public IAsyncRelayCommand UpdateSelectedServerCommand { get; private set; } = null!;
    public IAsyncRelayCommand RestoreSnapshotCommand { get; private set; } = null!;
    public IRelayCommand DeleteSnapshotCommand { get; private set; } = null!;
    public IAsyncRelayCommand CopyLocalAddressCommand { get; private set; } = null!;
    public IAsyncRelayCommand CopyLanAddressCommand { get; private set; } = null!;
    public IAsyncRelayCommand CopyOrbitraAddressCommand { get; private set; } = null!;
    public IAsyncRelayCommand ExportConsoleCommand { get; private set; } = null!;
    public IAsyncRelayCommand CopyConsoleLineCommand { get; private set; } = null!;
    public IRelayCommand GoToFirstErrorCommand { get; private set; } = null!;
    public IRelayCommand ToggleConsoleBookmarkCommand { get; private set; } = null!;
    public IRelayCommand<string> QuickConsoleCommand { get; private set; } = null!;
    public IRelayCommand<string> ToggleConsoleFilterCommand { get; private set; } = null!;

    private void InitializeAdvancedFeatures()
    {
        Directory.CreateDirectory(_snapshotsRoot);
        WizardNextCommand = new AsyncRelayCommand(AdvanceWizardAsync, () => WizardCanGoNext);
        WizardTestLaunchCommand = new AsyncRelayCommand(TestWizardLaunchAsync, () => WizardValidationStep && WizardProfile != null && !IsBusy);
        WizardBackCommand = new RelayCommand(() => WizardStep--, () => WizardCanGoBack);
        WizardFinishCommand = new RelayCommand(FinishServerWizard, () => WizardReadyStep && WizardProfile != null);
        CreateSnapshotCommand = new AsyncRelayCommand(() => CreateSnapshotAsync(false), () => SelectedServer is { IsRunning: false });
        UpdateSelectedServerCommand = new AsyncRelayCommand(UpdateSelectedServerAsync, CanUpdateSelectedServer);
        RestoreSnapshotCommand = new AsyncRelayCommand(RestoreSnapshotAsync, () => SelectedServer is { IsRunning: false } && SelectedSnapshot != null);
        DeleteSnapshotCommand = new RelayCommand(DeleteSnapshot, () => SelectedSnapshot != null);
        CopyLocalAddressCommand = new AsyncRelayCommand(() => CopyAddressAsync(AddressKind.Local), () => HasSelection);
        CopyLanAddressCommand = new AsyncRelayCommand(() => CopyAddressAsync(AddressKind.Lan), () => HasSelection);
        CopyOrbitraAddressCommand = new AsyncRelayCommand(() => CopyAddressAsync(AddressKind.Orbitra), () => HasSelection);
        ExportConsoleCommand = new AsyncRelayCommand(ExportConsoleAsync, () => HasSelection);
        CopyConsoleLineCommand = new AsyncRelayCommand(CopySelectedConsoleLineAsync, () => SelectedConsoleEntry != null);
        GoToFirstErrorCommand = new RelayCommand(GoToFirstError, () => ConsoleErrorCount > 0);
        ToggleConsoleBookmarkCommand = new RelayCommand(ToggleSelectedBookmark, () => SelectedConsoleEntry != null);
        QuickConsoleCommand = new RelayCommand<string>(SendQuickCommand, command => CanStop && !string.IsNullOrWhiteSpace(command));
        ToggleConsoleFilterCommand = new RelayCommand<string>(ToggleConsoleFilter);
        InitializeConfigurationFeatures();
    }

    private void OpenServerWizard()
    {
        WizardProfile = null;
        WizardStep = 0;
        WizardStatus = "Выберите ZIP, готовую папку или совместимый CDN.";
        IsAddPanelOpen = true;
    }

    private void CancelServerWizard()
    {
        var draft = WizardProfile;
        WizardProfile = null;
        WizardStep = 0;
        IsAddPanelOpen = false;
        if (draft is { ManagedFiles: true } && Servers.All(x => x.Id != draft.Id))
        {
            try { if (Directory.Exists(draft.Directory)) Directory.Delete(draft.Directory, true); } catch { }
        }
    }

    private void PrepareWizardProfile(LocalServerProfileViewModel profile, string status)
    {
        var previous = WizardProfile;
        if (previous is { ManagedFiles: true } && previous.Id != profile.Id && Servers.All(x => x.Id != previous.Id))
        {
            try { if (Directory.Exists(previous.Directory)) Directory.Delete(previous.Directory, true); } catch { }
        }
        WizardProfile = profile;
        WizardStep = 1;
        WizardStatus = status;
        StatusText = status;
    }

    private async Task AdvanceWizardAsync()
    {
        var profile = WizardProfile;
        if (profile == null) return;
        switch (WizardStep)
        {
            case 1:
                WizardStep = 2; WizardStatus = "Выберите найденный файл запуска."; break;
            case 2:
                if (string.IsNullOrWhiteSpace(profile.LaunchFile)) { WizardStatus = "Выберите файл запуска."; return; }
                WizardStep = 3; WizardStatus = profile.ConfigFiles.Count == 0 ? "Конфигурации не найдены, этот шаг можно пропустить." : "Выберите основную конфигурацию."; break;
            case 3:
                WizardStep = 4; WizardStatus = "Задайте понятное название и порт сервера."; break;
            case 4:
                if (string.IsNullOrWhiteSpace(profile.Name)) { WizardStatus = "Название не может быть пустым."; return; }
                if (profile.Port is < 1 or > 65535) { WizardStatus = "Порт должен быть от 1 до 65535."; return; }
                WizardStep = 5;
                await ValidateWizardProfileAsync(profile);
                break;
            case 5:
                await TestWizardLaunchAsync();
                if (profile.LaunchTestPassed) { WizardStep = 6; WizardStatus = "Профиль готов. Проверьте параметры и завершите мастер."; }
                break;
        }
    }

    private async Task ValidateWizardProfileAsync(LocalServerProfileViewModel profile)
    {
        IsBusy = true;
        try
        {
            WizardStatus = "Проверка файла запуска и порта…";
            var launch = ResolveInsideServer(profile, profile.LaunchFile);
            if (!File.Exists(launch)) throw new FileNotFoundException("Файл запуска больше не существует.", launch);
            var portOpen = await IsPortOpenAsync(profile.Port);
            if (portOpen) throw new InvalidOperationException($"Порт {profile.Port} уже занят другим приложением.");
            profile.ValidationPassed = true;
            profile.LaunchTestPassed = false;
            profile.ValidationText = "Файлы и порт проверены. Выполните пробный запуск.";
            WizardStatus = "Базовая проверка пройдена. Теперь нужен короткий пробный запуск.";
        }
        catch (Exception e)
        {
            profile.ValidationPassed = false;
            profile.ValidationText = e.Message;
            WizardStatus = "Проверка не пройдена: " + e.Message;
        }
        finally { IsBusy = false; NotifyWizard(); }
    }

    private async Task TestWizardLaunchAsync()
    {
        var profile = WizardProfile;
        if (profile == null) return;
        await ValidateWizardProfileAsync(profile);
        if (!profile.ValidationPassed) return;
        IsBusy = true;
        Process? process = null;
        WindowsJobObject? jobObject = null;
        string? jobGate = null;
        try
        {
            profile.ValidationText = "Пробный запуск процесса…";
            WizardStatus = "Запускаем сервер на несколько секунд и проверяем раннее аварийное завершение.";
            var info = CreateWizardTestStartInfo(profile, out jobGate);
            process = new Process { StartInfo = info };
            if (!process.Start()) throw new InvalidOperationException("Операционная система не создала процесс сервера.");
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    jobObject = WindowsJobObject.CreateAndAssign(process);
                    File.WriteAllText(jobGate!, "ready", new UTF8Encoding(false));
                    jobGate = null;
                }
                catch
                {
                    try { process.Kill(true); } catch { }
                    throw;
                }
            }
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var exited = process.WaitForExitAsync();
            var completed = await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(6)));
            if (completed == exited)
                throw new InvalidOperationException($"Процесс слишком рано завершился с кодом {process.ExitCode}.");

            profile.LaunchTestPassed = true;
            profile.ValidationText = "Пробный запуск выполнен успешно";
            WizardStatus = "Процесс стабильно работает. Выполняется штатная остановка тестового запуска.";
            try { await process.StandardInput.WriteLineAsync("shutdown"); await process.StandardInput.FlushAsync(); } catch { }
            var stopped = process.WaitForExitAsync();
            if (await Task.WhenAny(stopped, Task.Delay(TimeSpan.FromSeconds(4))) != stopped && !process.HasExited)
            {
                if (jobObject != null) jobObject.Terminate(); else process.Kill(true);
            }
        }
        catch (Exception e)
        {
            profile.LaunchTestPassed = false;
            profile.ValidationPassed = false;
            profile.ValidationText = e.Message;
            WizardStatus = "Пробный запуск не пройден: " + e.Message;
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try { if (jobObject != null) jobObject.Terminate(); else process.Kill(true); } catch { }
            }
            jobObject?.Dispose();
            process?.Dispose();
            if (jobGate != null) try { File.Delete(jobGate); } catch { }
            IsBusy = false;
            NotifyWizard();
        }
    }

    private static ProcessStartInfo CreateWizardTestStartInfo(LocalServerProfileViewModel profile, out string? jobGate)
    {
        jobGate = null;
        var launchPath = ResolveInsideServer(profile, profile.LaunchFile);
        var info = new ProcessStartInfo
        {
            WorkingDirectory = Path.GetDirectoryName(launchPath)!, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        var testArguments = profile.Arguments.Replace('\r', ' ').Replace('\n', ' ');
        if (OperatingSystem.IsWindows())
        {
            var wrapper = Path.Combine(Path.GetDirectoryName(launchPath)!, ".orbitra-wizard-test.cmd");
            jobGate = Path.Combine(Path.GetDirectoryName(launchPath)!, $".orbitra-wizard-job-{Guid.NewGuid():N}.ready");
            var wrapperText = "@echo off\r\nchcp 65001 >nul\r\n:orbitra_wait_job\r\nif exist \"" + jobGate + "\" goto orbitra_start\r\n>nul 2>nul ping 127.0.0.1 -n 2 -w 50\r\ngoto orbitra_wait_job\r\n:orbitra_start\r\ndel /q \"" + jobGate + "\" >nul 2>nul\r\ncall \"" + Path.GetFileName(launchPath) + "\" " + testArguments + "\r\nexit /b %errorlevel%\r\n";
            File.WriteAllText(wrapper, wrapperText, new UTF8Encoding(false));
            info.FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe";
            info.ArgumentList.Add("/D"); info.ArgumentList.Add("/Q"); info.ArgumentList.Add("/C"); info.ArgumentList.Add(wrapper);
        }
        else
        {
            info.FileName = launchPath;
            info.Arguments = testArguments;
        }
        return info;
    }

    private void FinishServerWizard()
    {
        var profile = WizardProfile;
        if (profile is not { ValidationPassed: true }) return;
        profile.PropertyChanged += ProfileChanged;
        Servers.Add(profile);
        SelectedServer = profile;
        WizardProfile = null;
        IsAddPanelOpen = false;
        SaveProfiles();
        RefreshVisibleServers();
        StatusText = $"Профиль «{profile.Name}» создан.";
        _main.ShowToast("Локальный сервер готов");
    }

    private void NotifyWizard()
    {
        OnPropertyChanged(nameof(WizardSourceStep)); OnPropertyChanged(nameof(WizardArchiveStep));
        OnPropertyChanged(nameof(WizardLaunchStep)); OnPropertyChanged(nameof(WizardConfigStep));
        OnPropertyChanged(nameof(WizardIdentityStep)); OnPropertyChanged(nameof(WizardValidationStep));
        OnPropertyChanged(nameof(WizardReadyStep)); OnPropertyChanged(nameof(WizardCanGoBack));
        OnPropertyChanged(nameof(WizardCanGoNext)); OnPropertyChanged(nameof(WizardStepText));
        OnPropertyChanged(nameof(WizardProgress));
        WizardNextCommand?.NotifyCanExecuteChanged(); WizardBackCommand?.NotifyCanExecuteChanged(); WizardFinishCommand?.NotifyCanExecuteChanged();
        WizardTestLaunchCommand?.NotifyCanExecuteChanged();
    }

    private async void AdvancedTick()
    {
        foreach (var server in Servers) server.RefreshAdvancedMetrics();
        if (_healthCheckRunning) return;
        var running = Servers.Where(x => x.IsRunning).ToArray();
        if (running.Length == 0) return;
        _healthCheckRunning = true;
        try { await Task.WhenAll(running.Select(RefreshHealthAsync)); }
        finally { _healthCheckRunning = false; }
    }

    private async Task RefreshHealthAsync(LocalServerProfileViewModel server)
    {
        var wasReady = server.IsReady;
        var portOpen = await IsPortOpenAsync(server.Port);
        server.IsPortOpen = portOpen;
        server.PortStatusText = portOpen ? $"Порт {server.Port} открыт" : $"Порт {server.Port} закрыт";
        if (portOpen)
        {
            server.LaunchStage = "Сервер готов";
            server.IsReady = true;
            if (!wasReady)
            {
                MarkConfigAsWorking(server);
                SystemNotificationService.Show("Локальный сервер готов", $"{server.Name} доступен на порту {server.Port}", connect: () => ConnectingViewModel.StartConnect(_main, $"ss14://127.0.0.1:{server.Port}"));
            }
        }
        else if (server.LastStartedUtc is { } started && DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(45))
        {
            server.LaunchStage = "Ожидание порта";
            server.IsReady = false;
            if (!server.PortWarningShown)
            {
                server.PortWarningShown = true;
                SystemNotificationService.Show("Сервер не открыл порт", $"{server.Name}: порт {server.Port} всё ещё закрыт");
            }
        }
        server.RefreshWarnings();
    }

    private static async Task<bool> IsPortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromMilliseconds(450));
            return true;
        }
        catch { return false; }
    }

    private void OnAdvancedSelectionChanged(LocalServerProfileViewModel? server)
    {
        RefreshAdvancedConsole();
        LoadSnapshots();
        OnPropertyChanged(nameof(ConsoleErrorCount)); OnPropertyChanged(nameof(ConsoleWarningCount));
        CreateSnapshotCommand?.NotifyCanExecuteChanged(); RestoreSnapshotCommand?.NotifyCanExecuteChanged();
        CopyLocalAddressCommand?.NotifyCanExecuteChanged(); CopyLanAddressCommand?.NotifyCanExecuteChanged(); CopyOrbitraAddressCommand?.NotifyCanExecuteChanged();
        UpdateSelectedServerCommand?.NotifyCanExecuteChanged();
        OnConfigurationSelectionChanged(server);
    }

    private void AddAdvancedConsoleLine(string line)
    {
        var server = SelectedServer;
        if (server == null) return;
        var entry = server.AdvancedConsoleHistory.LastOrDefault();
        if (entry != null && ConsoleMatches(entry)) FilteredConsoleEntries.Add(entry);
        OnPropertyChanged(nameof(ConsoleErrorCount)); OnPropertyChanged(nameof(ConsoleWarningCount));
        GoToFirstErrorCommand.NotifyCanExecuteChanged();
    }

    private void RefreshAdvancedConsole()
    {
        FilteredConsoleEntries.Clear();
        if (SelectedServer == null) return;
        foreach (var entry in SelectedServer.AdvancedConsoleHistory.Where(ConsoleMatches)) FilteredConsoleEntries.Add(entry);
    }

    private bool ConsoleMatches(LocalConsoleLineViewModel entry)
    {
        if (ShowBookmarksOnly && !entry.IsBookmarked) return false;
        if (!string.IsNullOrWhiteSpace(ConsoleSearch) && !entry.Text.Contains(ConsoleSearch.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return entry.Level switch
        {
            LocalConsoleLevel.Error => ShowErrors,
            LocalConsoleLevel.Warning => ShowWarnings,
            LocalConsoleLevel.Debug => ShowDebug,
            _ => ShowInfo
        };
    }

    private void ClearAdvancedConsole()
    {
        SelectedServer?.AdvancedConsoleHistory.Clear();
        FilteredConsoleEntries.Clear();
        OnPropertyChanged(nameof(ConsoleErrorCount)); OnPropertyChanged(nameof(ConsoleWarningCount));
    }

    private void TrimConsoleHistory()
    {
        if (SelectedServer == null) return;
        while (SelectedServer.ConsoleHistory.Count > ConsoleLineLimit) SelectedServer.ConsoleHistory.RemoveAt(0);
        while (SelectedServer.AdvancedConsoleHistory.Count > ConsoleLineLimit) SelectedServer.AdvancedConsoleHistory.RemoveAt(0);
        while (ConsoleLines.Count > ConsoleLineLimit) ConsoleLines.RemoveAt(0);
        RefreshAdvancedConsole();
    }

    private void RememberConsoleCommand(string command)
    {
        if (_commandHistory.LastOrDefault() != command) _commandHistory.Add(command);
        _commandHistoryIndex = _commandHistory.Count;
    }

    public void NavigateCommandHistory(int direction)
    {
        if (_commandHistory.Count == 0) return;
        _commandHistoryIndex = Math.Clamp(_commandHistoryIndex + direction, 0, _commandHistory.Count);
        ConsoleCommand = _commandHistoryIndex == _commandHistory.Count ? string.Empty : _commandHistory[_commandHistoryIndex];
    }

    private void SendQuickCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        ConsoleCommand = command;
        if (SendCommand.CanExecute(null)) SendCommand.Execute(null);
    }

    private void ToggleConsoleFilter(string? filter)
    {
        switch (filter?.ToLowerInvariant())
        {
            case "info": ShowInfo = !ShowInfo; break;
            case "warn": ShowWarnings = !ShowWarnings; break;
            case "error": ShowErrors = !ShowErrors; break;
            case "debug": ShowDebug = !ShowDebug; break;
            case "bookmark": ShowBookmarksOnly = !ShowBookmarksOnly; break;
        }
    }

    private async Task CopySelectedConsoleLineAsync()
    {
        if (_main.Control?.Clipboard == null || SelectedConsoleEntry == null) return;
        await _main.Control.Clipboard.SetTextAsync(SelectedConsoleEntry.Text);
        _main.ShowToast("Строка консоли скопирована");
    }

    private void ToggleSelectedBookmark()
    {
        if (SelectedConsoleEntry == null) return;
        SelectedConsoleEntry.IsBookmarked = !SelectedConsoleEntry.IsBookmarked;
        if (ShowBookmarksOnly) RefreshAdvancedConsole();
    }

    private void GoToFirstError()
    {
        ShowErrors = true; ShowBookmarksOnly = false;
        SelectedConsoleEntry = FilteredConsoleEntries.FirstOrDefault(x => x.Level == LocalConsoleLevel.Error);
    }

    private async Task ExportConsoleAsync()
    {
        if (_main.Control?.StorageProvider is not { } storage || SelectedServer == null) return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Экспорт консоли", SuggestedFileName = $"{SelectedServer.Name}-console-{DateTime.Now:yyyyMMdd-HHmm}.log", DefaultExtension = "log",
            FileTypeChoices = [new FilePickerFileType("Журнал") { Patterns = ["*.log", "*.txt"] }]
        });
        if (file == null) return;
        await File.WriteAllLinesAsync(file.Path.LocalPath, SelectedServer.AdvancedConsoleHistory.Select(x => $"[{x.Time:HH:mm:ss}] [{x.Level}] {x.Text}"), Encoding.UTF8);
        _main.ShowToast("Консоль экспортирована");
    }

    private async Task CopyAddressAsync(AddressKind kind)
    {
        if (_main.Control?.Clipboard == null || SelectedServer == null) return;
        var host = kind == AddressKind.Lan ? GetLanAddress() : "127.0.0.1";
        var address = $"ss14://{host}:{SelectedServer.Port}";
        if (kind == AddressKind.Orbitra) address = "orbitra://connect/" + Uri.EscapeDataString(address);
        await _main.Control.Clipboard.SetTextAsync(address);
        _main.ShowToast("Адрес скопирован: " + address);
    }

    private static string GetLanAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType is not NetworkInterfaceType.Loopback)
                .SelectMany(x => x.GetIPProperties().UnicastAddresses)
                .Select(x => x.Address)
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(x))?.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1"; }
    }

    private async Task<bool> CreateSnapshotAsync(bool isAutomatic, string? automaticDescription = null)
    {
        var server = SelectedServer;
        if (server == null || server.IsRunning) return false;
        var created = false;
        IsBusy = true;
        try
        {
            var targetDir = Path.Combine(_snapshotsRoot, server.Id);
            Directory.CreateDirectory(targetDir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = Path.Combine(targetDir, stamp + ".zip");
            var files = EnumerateSnapshotFiles(server).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (files.Count == 0) throw new InvalidOperationException("Для выбранного типа снимка не найдено файлов.");
            await Task.Run(() =>
            {
                using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                foreach (var source in files)
                {
                    try
                    {
                        var relative = Path.GetRelativePath(server.Directory, source);
                        if (relative.StartsWith("..")) continue;
                        archive.CreateEntryFromFile(source, relative, CompressionLevel.Fastest);
                    }
                    catch { }
                }
            });
            var meta = new SnapshotMetadata(DateTimeOffset.Now, isAutomatic ? automaticDescription ?? "Автоматический снимок перед запуском" : SnapshotDescription.Trim(), SnapshotScopeText, new FileInfo(path).Length);
            await File.WriteAllTextAsync(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(meta, JsonOptions));
            SnapshotDescription = string.Empty;
            EnforceSnapshotRetention(server);
            LoadSnapshots();
            StatusText = "Снимок создан.";
            created = true;
        }
        catch (Exception e) { StatusText = "Не удалось создать снимок: " + e.Message; _main.ShowToast(StatusText, true); }
        finally { IsBusy = false; }
        return created;
    }

    private IEnumerable<string> EnumerateSnapshotFiles(LocalServerProfileViewModel server)
    {
        var all = Directory.EnumerateFiles(server.Directory, "*", SearchOption.AllDirectories);
        if (SnapshotFullBuild) return all;
        return all.Where(path =>
        {
            var relative = Path.GetRelativePath(server.Directory, path).Replace('\\', '/');
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (SnapshotConfiguration && (ext is ".toml" or ".yml" or ".yaml" or ".json" or ".cfg")) return true;
            if (SnapshotDatabase && (ext is ".db" or ".sqlite" or ".sqlite3")) return true;
            if (SnapshotSaves && Regex.IsMatch(relative, @"(^|/)(save|saves|data)(/|$)", RegexOptions.IgnoreCase)) return true;
            if (SnapshotLogs && (ext is ".log" or ".txt") && relative.Contains("log", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        });
    }

    private string SnapshotScopeText => SnapshotFullBuild ? "Вся сборка" : string.Join(", ", new[]
    {
        SnapshotConfiguration ? "Конфигурация" : null, SnapshotDatabase ? "База данных" : null,
        SnapshotSaves ? "Сохранения" : null, SnapshotLogs ? "Журналы" : null
    }.Where(x => x != null));

    private void LoadSnapshots()
    {
        Snapshots.Clear(); SelectedSnapshot = null;
        var server = SelectedServer;
        if (server == null) return;
        var directory = Path.Combine(_snapshotsRoot, server.Id);
        if (!Directory.Exists(directory)) return;
        foreach (var zip in Directory.EnumerateFiles(directory, "*.zip").OrderByDescending(File.GetCreationTimeUtc))
        {
            SnapshotMetadata? meta = null;
            try
            {
                var metaPath = Path.ChangeExtension(zip, ".json");
                if (File.Exists(metaPath)) meta = JsonSerializer.Deserialize<SnapshotMetadata>(File.ReadAllText(metaPath));
            }
            catch { }
            var info = new FileInfo(zip);
            Snapshots.Add(new LocalSnapshotViewModel(zip, meta?.CreatedAt ?? info.CreationTime, meta?.Description ?? "Ручной снимок", meta?.Scope ?? "Неизвестно", info.Length));
        }
    }

    private async Task RestoreSnapshotAsync()
    {
        var server = SelectedServer; var snapshot = SelectedSnapshot;
        if (server == null || snapshot == null || server.IsRunning) return;
        IsBusy = true;
        try
        {
            await Task.Run(() => ExtractSafely(snapshot.Path, server.Directory));
            server.LaunchFiles = new ObservableCollection<string>(FindLaunchFiles(server.Directory));
            server.ConfigFiles = new ObservableCollection<string>(FindConfigFiles(server.Directory));
            StatusText = $"Снимок от {snapshot.CreatedText} восстановлен.";
            _main.ShowToast("Снимок восстановлен");
        }
        catch (Exception e) { StatusText = "Не удалось восстановить снимок: " + e.Message; _main.ShowToast(StatusText, true); }
        finally { IsBusy = false; }
    }

    private void DeleteSnapshot()
    {
        if (SelectedSnapshot == null) return;
        try { File.Delete(SelectedSnapshot.Path); File.Delete(Path.ChangeExtension(SelectedSnapshot.Path, ".json")); }
        catch (Exception e) { StatusText = "Не удалось удалить снимок: " + e.Message; }
        LoadSnapshots();
    }

    private void EnforceSnapshotRetention(LocalServerProfileViewModel server)
    {
        var directory = Path.Combine(_snapshotsRoot, server.Id);
        foreach (var file in Directory.EnumerateFiles(directory, "*.zip").OrderByDescending(File.GetCreationTimeUtc).Skip(Math.Clamp(server.SnapshotRetention, 1, 100)))
        {
            try { File.Delete(file); File.Delete(Path.ChangeExtension(file, ".json")); } catch { }
        }
    }

    private bool CanUpdateSelectedServer() => SelectedServer is { IsRunning: false, ManagedFiles: true } server
                                               && Uri.TryCreate(server.SourceManifest, UriKind.Absolute, out _)
                                               && !IsBusy;

    private async Task UpdateSelectedServerAsync()
    {
        var server = SelectedServer;
        if (server == null || !CanUpdateSelectedServer()) return;
        var previousFull = SnapshotFullBuild;
        SnapshotFullBuild = true;
        if (!await CreateSnapshotAsync(true, "Автоматический снимок перед обновлением CDN"))
        {
            SnapshotFullBuild = previousFull;
            return;
        }
        SnapshotFullBuild = previousFull;

        IsBusy = true;
        var tempArchive = Path.Combine(Path.GetTempPath(), "orbitra-server-update-" + Guid.NewGuid().ToString("N") + ".zip");
        var staging = Path.Combine(_root, ".update-" + Guid.NewGuid().ToString("N"));
        string? oldDirectory = null;
        try
        {
            StatusText = "Получение последней версии CDN…";
            using var manifestStream = await _http.GetStreamAsync(server.SourceManifest);
            using var document = await JsonDocument.ParseAsync(manifestStream);
            var latest = document.RootElement.GetProperty("builds").EnumerateObject()
                .Select(build => new { build.Name, Data = build.Value, Time = build.Value.GetProperty("time").GetDateTimeOffset() })
                .OrderByDescending(x => x.Time).First();
            var package = latest.Data.GetProperty("server").GetProperty("win-x64");
            var url = package.GetProperty("url").GetString() ?? throw new InvalidDataException("CDN не содержит Windows x64-сборку.");
            var expectedHash = package.TryGetProperty("sha256", out var hashNode) ? hashNode.GetString() : null;
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(tempArchive);
                await input.CopyToAsync(output);
            }
            if (!string.IsNullOrWhiteSpace(expectedHash))
            {
                await using var input = File.OpenRead(tempArchive);
                var actual = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(input));
                if (!actual.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SHA-256 обновления не совпадает с manifest.");
            }

            StatusText = "Проверка и подготовка новой сборки…";
            Directory.CreateDirectory(staging);
            await Task.Run(() => ExtractSafely(tempArchive, staging));
            var launchFiles = FindLaunchFiles(staging).ToList();
            if (launchFiles.Count == 0) throw new InvalidDataException("В обновлении не найден файл запуска.");
            var configs = FindConfigFiles(staging).ToList();

            oldDirectory = server.Directory + ".orbitra-old-" + Guid.NewGuid().ToString("N");
            Directory.Move(server.Directory, oldDirectory);
            Directory.Move(staging, server.Directory);
            server.LaunchFiles = new ObservableCollection<string>(launchFiles);
            server.ConfigFiles = new ObservableCollection<string>(configs);
            if (!launchFiles.Contains(server.LaunchFile, StringComparer.OrdinalIgnoreCase)) server.LaunchFile = launchFiles.First();
            if (!configs.Contains(server.ConfigFile, StringComparer.OrdinalIgnoreCase)
                || configs.FirstOrDefault() is { } preferred && Path.GetFileName(preferred).Equals("server_config.toml", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(server.ConfigFile).Equals("server_config.toml", StringComparison.OrdinalIgnoreCase))
                server.ConfigFile = configs.FirstOrDefault() ?? string.Empty;
            server.Version = latest.Name;
            SaveProfiles();
            try { Directory.Delete(oldDirectory, true); oldDirectory = null; } catch { }
            StatusText = $"Сервер «{server.Name}» обновлён до {latest.Name}.";
            SystemNotificationService.Show("Локальный сервер обновлён", $"{server.Name}: {latest.Name}");
        }
        catch (Exception e)
        {
            if (oldDirectory != null && Directory.Exists(oldDirectory) && !Directory.Exists(server.Directory))
                try { Directory.Move(oldDirectory, server.Directory); oldDirectory = null; } catch { }
            StatusText = "Обновление не выполнено: " + e.Message;
            _main.ShowToast(StatusText, true);
        }
        finally
        {
            try { File.Delete(tempArchive); } catch { }
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            IsBusy = false;
        }
    }

    private enum AddressKind { Local, Lan, Orbitra }
    private sealed record SnapshotMetadata(DateTimeOffset CreatedAt, string Description, string Scope, long Size);
}

public sealed partial class LocalServerProfileViewModel
{
    [ObservableProperty] private bool _autoSnapshotBeforeStart;
    [ObservableProperty] private int _snapshotRetention = 8;
    [ObservableProperty] private string _launchStage = "Остановлен";
    [ObservableProperty] private string _portStatusText = "Не проверен";
    [ObservableProperty] private bool _isPortOpen;
    [ObservableProperty] private bool _isReady;
    [ObservableProperty] private string _warningText = "Нарушений не обнаружено";
    [ObservableProperty] private bool _hasWarnings;
    [ObservableProperty] private bool _validationPassed;
    [ObservableProperty] private bool _launchTestPassed;
    [ObservableProperty] private string _validationText = "Ожидает проверки";
    [ObservableProperty] private string _cpuText = "—";
    [ObservableProperty] private string _threadCountText = "—";
    [ObservableProperty] private string _diskWriteText = "—";
    [ObservableProperty] private string _diskWrittenTotalText = "—";
    [ObservableProperty] private string _logGrowthText = "—";
    [ObservableProperty] private string _memoryGraphData = "M 0,34 L 100,34";
    [ObservableProperty] private string _cpuGraphData = "M 0,34 L 100,34";
    [ObservableProperty] private string _diskGraphData = "M 0,34 L 100,34";
    [ObservableProperty] private string _logGraphData = "M 0,34 L 100,34";

    public ObservableCollection<LocalConsoleLineViewModel> AdvancedConsoleHistory { get; } = [];
    internal bool StopRequested { get; set; }
    internal WindowsJobObject? JobObject { get; set; }
    internal bool PortWarningShown { get; set; }
    internal int MetricsProcessId { get; private set; }
    internal double CurrentMemoryMb { get; private set; } = -1;
    internal bool ProcessTreeResponding { get; private set; } = true;
    internal DateTimeOffset LastConsoleUtc { get; private set; } = DateTimeOffset.UtcNow;
    private readonly Queue<double> _memorySamples = new();
    private readonly Queue<double> _cpuSamples = new();
    private readonly Queue<double> _diskSamples = new();
    private readonly Queue<double> _logSamples = new();
    private TimeSpan _previousCpu;
    private DateTimeOffset _previousMetricAt;
    private ulong _previousWriteBytes;
    private long _logBytesThisSecond;

    internal void MarkStarted()
    {
        _previousCpu = TimeSpan.Zero; _previousMetricAt = DateTimeOffset.UtcNow; _previousWriteBytes = 0;
        MetricsProcessId = 0; CurrentMemoryMb = -1; ProcessTreeResponding = true;
        LastConsoleUtc = DateTimeOffset.UtcNow;
        IsReady = false; IsPortOpen = false; PortWarningShown = false; WarningText = "Ожидание готовности"; HasWarnings = false;
    }

    internal void RecordLogLine(string line)
    {
        LastConsoleUtc = DateTimeOffset.UtcNow;
        _logBytesThisSecond += Encoding.UTF8.GetByteCount(line) + 1;
        var level = ParseLevel(line);
        AdvancedConsoleHistory.Add(new LocalConsoleLineViewModel(DateTimeOffset.Now, line, level));
        while (AdvancedConsoleHistory.Count > 20000) AdvancedConsoleHistory.RemoveAt(0);
        if (!IsReady && Regex.IsMatch(line, "map|round|server started|ready", RegexOptions.IgnoreCase)) LaunchStage = "Загрузка карты";
    }

    internal void RefreshAdvancedMetrics()
    {
        if (Process is not { HasExited: false } process) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var elapsed = Math.Max(0.1, (now - _previousMetricAt).TotalSeconds);
            var tree = CaptureProcessTree(process);
            var totalCpu = tree.TotalCpu;
            var cpuDelta = Math.Max(0, (totalCpu - _previousCpu).TotalSeconds);
            var cpu = _previousMetricAt == default ? 0 : Math.Clamp(cpuDelta / elapsed / Environment.ProcessorCount * 100, 0, 100);
            _previousCpu = totalCpu; _previousMetricAt = now;
            var memoryMb = tree.WorkingSetBytes / 1024d / 1024d;
            var writeBytes = tree.WriteBytes;
            var diskRate = _previousWriteBytes == 0 || writeBytes < _previousWriteBytes ? 0 : (writeBytes - _previousWriteBytes) / elapsed;
            _previousWriteBytes = writeBytes;
            var logRate = _logBytesThisSecond / elapsed; _logBytesThisSecond = 0;
            Push(_memorySamples, memoryMb); Push(_cpuSamples, cpu); Push(_diskSamples, diskRate); Push(_logSamples, logRate);
            MemoryGraphData = Graph(_memorySamples); CpuGraphData = Graph(_cpuSamples, 100); DiskGraphData = Graph(_diskSamples); LogGraphData = Graph(_logSamples);
            MetricsProcessId = tree.MainProcessId;
            CurrentMemoryMb = memoryMb;
            ProcessTreeResponding = tree.Responding;
            CpuText = $"{cpu:F1}%"; ThreadCountText = tree.ThreadCount.ToString("N0");
            DiskWriteText = FormatRate(diskRate); LogGrowthText = FormatRate(logRate);
            DiskWrittenTotalText = FormatBytes(writeBytes);
            OnPropertyChanged(nameof(MemoryText)); OnPropertyChanged(nameof(ProcessIdText));
        }
        catch { }
    }

    internal void RefreshWarnings()
    {
        var warnings = new List<string>();
        try
        {
            if (Process is { HasExited: false } process)
            {
                if (CurrentMemoryMb > 2048) warnings.Add("Память выше 2 ГБ");
                if (!ProcessTreeResponding) warnings.Add("Процесс не отвечает");
            }
        }
        catch { }
        if (IsRunning && DateTimeOffset.UtcNow - LastConsoleUtc > TimeSpan.FromMinutes(2)) warnings.Add("Консоль молчит более 2 минут");
        if (IsRunning && LastStartedUtc is { } started && DateTimeOffset.UtcNow - started > TimeSpan.FromSeconds(45) && !IsPortOpen) warnings.Add("Порт закрыт после запуска");
        WarningText = warnings.Count == 0 ? "Нарушений не обнаружено" : string.Join(" · ", warnings);
        HasWarnings = warnings.Count > 0;
    }

    private static LocalConsoleLevel ParseLevel(string line)
    {
        if (Regex.IsMatch(line, @"\b(ERR|ERROR|FTL|FATAL)\b", RegexOptions.IgnoreCase)) return LocalConsoleLevel.Error;
        if (Regex.IsMatch(line, @"\b(WRN|WARN|WARNING)\b", RegexOptions.IgnoreCase)) return LocalConsoleLevel.Warning;
        if (Regex.IsMatch(line, @"\b(DBG|DEBUG|VRB|VERBOSE)\b", RegexOptions.IgnoreCase)) return LocalConsoleLevel.Debug;
        return LocalConsoleLevel.Info;
    }

    private static void Push(Queue<double> queue, double value) { queue.Enqueue(value); while (queue.Count > 36) queue.Dequeue(); }
    private static string Graph(IEnumerable<double> source, double? fixedMax = null)
    {
        var values = source.ToArray(); if (values.Length < 2) return "M 0,34 L 100,34";
        var max = Math.Max(1, fixedMax ?? values.Max());
        return string.Join(" ", values.Select((value, i) =>
        {
            var x = (i * 100d / (values.Length - 1)).ToString("F2", CultureInfo.InvariantCulture);
            var y = (34 - Math.Clamp(value / max, 0, 1) * 32).ToString("F2", CultureInfo.InvariantCulture);
            return $"{(i == 0 ? "M" : "L")} {x},{y}";
        }));
    }
    private static string FormatRate(double bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024 / 1024:F1} МБ/с" : bytes >= 1024 ? $"{bytes / 1024:F1} КБ/с" : $"{bytes:F0} Б/с";
    private static string FormatBytes(ulong bytes) => bytes >= 1024UL * 1024 * 1024 ? $"{bytes / 1024d / 1024d / 1024d:F2} ГБ" : bytes >= 1024UL * 1024 ? $"{bytes / 1024d / 1024d:F1} МБ" : $"{bytes / 1024d:F0} КБ";

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessIoCounters(IntPtr handle, out IoCounters counters);
    private static ulong TryGetWriteBytes(Process process)
    {
        try { return OperatingSystem.IsWindows() && GetProcessIoCounters(process.Handle, out var counters) ? counters.WriteBytes : 0; }
        catch { return 0; }
    }

    private static ProcessTreeMetrics CaptureProcessTree(Process root)
    {
        var ids = OperatingSystem.IsWindows() ? GetDescendantProcessIds(root.Id) : new HashSet<int> { root.Id };
        ids.Add(root.Id);
        long memory = 0;
        var cpu = TimeSpan.Zero;
        var threads = 0;
        ulong writes = 0;
        var responding = true;
        var mainPid = root.Id;
        long mainMemory = -1;
        foreach (var id in ids)
        {
            try
            {
                using var process = Process.GetProcessById(id);
                if (process.HasExited) continue;
                process.Refresh();
                var workingSet = process.WorkingSet64;
                memory += workingSet;
                cpu += process.TotalProcessorTime;
                threads += process.Threads.Count;
                writes += TryGetWriteBytes(process);
                try { responding &= process.Responding; } catch { }
                if (workingSet > mainMemory) { mainMemory = workingSet; mainPid = id; }
            }
            catch { }
        }
        return new ProcessTreeMetrics(memory, cpu, threads, writes, responding, mainPid);
    }

    private static HashSet<int> GetDescendantProcessIds(int rootId)
    {
        var result = new HashSet<int>();
        var parents = new Dictionary<int, List<int>>();
        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == new IntPtr(-1)) return result;
        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry)) return result;
            do
            {
                var parent = unchecked((int)entry.ParentProcessId);
                var child = unchecked((int)entry.ProcessId);
                if (!parents.TryGetValue(parent, out var children)) parents[parent] = children = [];
                children.Add(child);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            } while (Process32Next(snapshot, ref entry));
        }
        finally { CloseHandle(snapshot); }

        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!parents.TryGetValue(parent, out var children)) continue;
            foreach (var child in children)
                if (result.Add(child)) queue.Enqueue(child);
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size, Usage, ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    private readonly record struct ProcessTreeMetrics(long WorkingSetBytes, TimeSpan TotalCpu, int ThreadCount, ulong WriteBytes, bool Responding, int MainProcessId);
}

public enum LocalConsoleLevel { Info, Warning, Error, Debug }

public sealed partial class LocalConsoleLineViewModel(DateTimeOffset time, string text, LocalConsoleLevel level) : ViewModelBase
{
    public DateTimeOffset Time { get; } = time;
    public string TimeText => Time.ToString("HH:mm:ss");
    public string Text { get; } = text;
    public LocalConsoleLevel Level { get; } = level;
    public string LevelText => Level switch { LocalConsoleLevel.Error => "ERROR", LocalConsoleLevel.Warning => "WARN", LocalConsoleLevel.Debug => "DEBUG", _ => "INFO" };
    public string LevelColor => Level switch { LocalConsoleLevel.Error => "#FF6565", LocalConsoleLevel.Warning => "#E6B94E", LocalConsoleLevel.Debug => "#8A8A8A", _ => "#79C995" };
    [ObservableProperty] private bool _isBookmarked;
}

public sealed class LocalSnapshotViewModel(string path, DateTimeOffset created, string description, string scope, long size)
{
    public string Path { get; } = path;
    public DateTimeOffset Created { get; } = created;
    public string CreatedText => Created.ToLocalTime().ToString("dd.MM.yyyy · HH:mm:ss");
    public string Description { get; } = string.IsNullOrWhiteSpace(description) ? "Без описания" : description;
    public string Scope { get; } = scope;
    public long Size { get; } = size;
    public string SizeText => Size >= 1024 * 1024 ? $"{Size / 1024d / 1024d:F1} МБ" : $"{Size / 1024d:F0} КБ";
}
