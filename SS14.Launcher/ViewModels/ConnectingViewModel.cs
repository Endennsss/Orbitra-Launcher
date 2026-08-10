using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Splat;
using SS14.Launcher.Localization;
using SS14.Launcher.Models;
using SS14.Launcher.Models.ContentManagement;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;
using static SS14.Launcher.Models.Connector.ConnectionStatus;

namespace SS14.Launcher.ViewModels;

public class ConnectingViewModel : ViewModelBase
{
    private readonly Connector _connector;
    private readonly Updater _updater;
    private readonly MainWindowViewModel _windowVm;
    private readonly ConnectionType _connectionType;
    private readonly LocalizationManager _loc;

    private readonly CancellationTokenSource _cancelSource = new CancellationTokenSource();

    private string? _reasonSuffix;
    private bool _errorToastShown;
    private bool _preflightRunning;
    private bool _preflightFailed;
    private bool _diagnosticsVisible;
    private string _diagnosticSummary = "Проверка ещё не запускалась";
    public string? TargetAddress { get; private set; }

    public ObservableCollection<LaunchDiagnosticItem> Diagnostics { get; } = new();
    public bool DiagnosticsVisible { get => _diagnosticsVisible; private set => SetProperty(ref _diagnosticsVisible, value); }
    public string DiagnosticSummary { get => _diagnosticSummary; private set => SetProperty(ref _diagnosticSummary, value); }

    public bool IsErrored
        => _preflightFailed || _connector.Status == ConnectionFailed ||
           _connector.Status == UpdateError ||
           _connector.Status == NotAContentBundle ||
           _connector is { Status: ClientExited, ClientExitedBadly: true };
    public bool IsConnected => _connector.Status == ClientRunning;

    public static event Action? StartedConnecting;

    public ConnectingViewModel(Connector connector, MainWindowViewModel windowVm, string? givenReason, ConnectionType connectionType)
    {
        _updater = Locator.Current.GetRequiredService<Updater>();
        _loc = LocalizationManager.Instance;
        _connector = connector;
        _windowVm = windowVm;
        _connectionType = connectionType;
        _reasonSuffix = (givenReason != null) ? ("\n" + givenReason) : "";

        _updater.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(_updater.Progress):
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(ProgressIndeterminate));
                    OnPropertyChanged(nameof(ProgressText));
                    break;

                case nameof(_updater.Speed):
                    OnPropertyChanged(nameof(SpeedText));
                    OnPropertyChanged(nameof(SpeedIndeterminate));
                    break;

                case nameof(_updater.Status):
                    OnPropertyChanged(nameof(StatusText));
                    break;
            }
        };

        _connector.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(_connector.Status):
                    OnPropertyChanged(nameof(ProgressIndeterminate));
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(ProgressBarVisible));
                    OnPropertyChanged(nameof(IsErrored));
                    OnPropertyChanged(nameof(IsAskingPrivacyPolicy));
                    OnPropertyChanged(nameof(IsConnected));

                    if (IsErrored && !_errorToastShown)
                    {
                        _errorToastShown = true;
                        _windowVm.ShowToast("Не удалось подключиться к серверу", true);
                        ActivityLog.Record("Подключение", "Ошибка запуска клиента", TargetAddress ?? "Неизвестный сервер", true);
                    }

                    if (_connector.Status == ClientExited)
                    {
                        PlaytimeTracker.Stop();
                        OrbitraProtocol.PublishPresence(null);
                        DiscordRichPresenceService.Instance.ShowLauncherAfterGame();
                    }

                    if (_connector.Status == ClientRunning)
                    {
                        PlaytimeTracker.Start(TargetAddress);
                        OrbitraProtocol.PublishPresence(TargetAddress);
                        ActivityLog.Record("Подключение", "Клиент запущен", TargetAddress ?? "Неизвестный сервер");
                        DiscordRichPresenceService.Instance.ShowPlaying();
                        CloseAfterConnected();
                    }
                    else if (_connector.Status == Cancelled
                        || _connector is { Status: ClientExited, ClientExitedBadly: false })
                    {
                        CloseOverlay();
                    }

                    break;
                case nameof(_connector.LastError):
                    OnPropertyChanged(nameof(ErrorDetails));
                    break;
                case nameof(_connector.PrivacyPolicyDifferentVersion):
                    OnPropertyChanged(nameof(PrivacyPolicyText));
                    break;
                case nameof(_connector.ClientExitedBadly):
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(IsErrored));
                    break;
            }
        };
    }

    public float Progress
    {
        get
        {
            if (_updater.Progress == null)
            {
                return 0;
            }

            var (downloaded, total, _) = _updater.Progress.Value;

            return downloaded / (float)total;
        }
    }

    public string ProgressText
    {
        get
        {
            if (_updater.Progress == null)
            {
                return "";
            }

            var (downloaded, total, unit) = _updater.Progress.Value;

            return unit switch
            {
                Updater.ProgressUnit.Bytes => $"{Helpers.FormatBytes(downloaded)} / {Helpers.FormatBytes(total)}",
                _ => $"{downloaded} / {total}"
            };
        }
    }

    public bool ProgressIndeterminate
        => _preflightRunning || _connector.Status != Updating
           || _updater.Progress == null;

    public bool ProgressBarVisible
        => _preflightRunning || (_connector.Status != ClientExited &&
           _connector.Status != ClientRunning &&
           _connector.Status != ConnectionFailed &&
           _connector.Status != UpdateError &&
           _connector.Status != NotAContentBundle);

    public bool SpeedIndeterminate => _connector.Status != Updating || _updater.Speed == null;

    public string SpeedText
    {
        get
        {
            if (_updater.Speed is not { } speed)
                return "";

            return $"{Helpers.FormatBytes(speed)}/s";
        }
    }

    public string StatusText
        => _preflightRunning ? "Диагностика перед запуском…" : _preflightFailed
            ? "Подключение остановлено · требуется исправление"
            : _connector.Status switch
        {
            None => "Подготовка подключения…",
            UpdateError => FormatUpdateError(),
            Updating => "Загрузка файлов · " + _loc.GetString(_updater.Status switch
            {
                Updater.UpdateStatus.CheckingClientUpdate => "connecting-update-status-checking-client-update",
                Updater.UpdateStatus.DownloadingEngineVersion => "connecting-update-status-downloading-engine",
                Updater.UpdateStatus.DownloadingClientUpdate => "connecting-update-status-downloading-content",
                Updater.UpdateStatus.FetchingClientManifest => "connecting-update-status-fetching-manifest",
                Updater.UpdateStatus.Verifying => "connecting-update-status-verifying",
                Updater.UpdateStatus.CullingEngine => "connecting-update-status-culling-engine",
                Updater.UpdateStatus.CullingContent => "connecting-update-status-culling-content",
                Updater.UpdateStatus.Ready => "connecting-update-status-ready",
                Updater.UpdateStatus.CheckingEngineModules => "connecting-update-status-checking-engine-modules",
                Updater.UpdateStatus.DownloadingEngineModules => "connecting-update-status-downloading-engine-modules",
                Updater.UpdateStatus.CommittingDownload => "connecting-update-status-committing-download",
                Updater.UpdateStatus.LoadingIntoDb => "connecting-update-status-loading-into-db",
                Updater.UpdateStatus.LoadingContentBundle => "connecting-update-status-loading-content-bundle",
                _ => "connecting-update-status-unknown"
            }) + _reasonSuffix,
            Connecting => "Проверка сервера…" + _reasonSuffix,
            ConnectionFailed => _loc.GetString("connecting-status-connection-failed"),
            StartingClient => "Запуск клиента…" + _reasonSuffix,
            ClientRunning => "Подключено",
            NotAContentBundle => _loc.GetString("connecting-status-not-a-content-bundle"),
            ClientExited => _connector.ClientExitedBadly
                ? _loc.GetString("connecting-status-client-crashed")
                : "",
            _ => ""
        };

    public string ErrorDetails => ClassifyError(_connector.LastError);

    public string SmartStageText => _preflightRunning ? "ПРОВЕРКА" : _connector.Status switch
    {
        Updating => "ЗАГРУЗКА",
        StartingClient => "ЗАПУСК",
        ClientRunning => "ГОТОВО",
        _ => "ПОДКЛЮЧЕНИЕ"
    };

    private string FormatUpdateError()
    {
        return _updater.UpdateException switch
        {
            NoEngineForPlatformException => _loc.GetString("connecting-status-update-error-no-engine-for-platform"),
            NoModuleForPlatformException => _loc.GetString("connecting-status-update-error-no-module-for-platform"),
            _ => _loc.GetString("connecting-status-update-error",
                ("err", _updater.UpdateException?.Message ?? _loc.GetString("connecting-status-update-error-unknown")))
        };
    }

    public string TitleText => _connectionType switch
    {
        ConnectionType.Server => _loc.GetString("connecting-title-connecting"),
        ConnectionType.ContentBundle => _loc.GetString("connecting-title-content-bundle"),
        _ => ""
    };

    public bool IsAskingPrivacyPolicy => _connector.Status == AwaitingPrivacyPolicyAcceptance;

    public string PrivacyPolicyText => _connector.PrivacyPolicyDifferentVersion
        ? _loc.GetString("connecting-privacy-policy-text-version-changed")
        : _loc.GetString("connecting-privacy-policy-text");

    public static void StartConnect(MainWindowViewModel windowVm, string address, string? givenReason = null)
    {
        var connector = new Connector();
        var vm = new ConnectingViewModel(connector, windowVm, givenReason, ConnectionType.Server);
        vm.TargetAddress = address;
        DiscordRichPresenceService.Instance.ShowConnecting(address);
        windowVm.ConnectingVM = vm;
        ActivityLog.Record("Подключение", "Начато подключение", address);
        vm.Start(address);
        StartedConnecting?.Invoke();
    }

    public static void StartContentBundle(MainWindowViewModel windowVm, IStorageFile file)
    {
        var connector = new Connector();
        var vm = new ConnectingViewModel(connector, windowVm, null, ConnectionType.ContentBundle);
        windowVm.ConnectingVM = vm;
        vm.StartContentBundle(file);
        StartedConnecting?.Invoke();
    }

    private async void Start(string address)
    {
        if (!await RunPreflightAsync(address))
            return;

        _connector.Connect(address, _cancelSource.Token);
    }

    private async Task<bool> RunPreflightAsync(string address)
    {
        _preflightRunning = true;
        _preflightFailed = false;
        Diagnostics.Clear();
        AddDiagnostic("Адрес сервера", "Проверяется…", DiagnosticState.Running);
        AddDiagnostic("Сеть", "Проверяется DNS и ответ сервера…", DiagnosticState.Running);
        AddDiagnostic("Компоненты", "Проверяется Loader и хранилище…", DiagnosticState.Running);
        AddDiagnostic("Аккаунт", "Проверяется активный профиль…", DiagnosticState.Running);
        NotifyPreflightChanged();

        try
        {
            if (!UriHelper.TryParseSs14Uri(address, out var serverUri))
                throw new InvalidOperationException("Адрес сервера имеет неверный формат.");
            SetDiagnostic(0, "Адрес сервера", serverUri.ToString(), DiagnosticState.Ready);

            var account = Locator.Current.GetRequiredService<LoginManager>().ActiveAccount;
            SetDiagnostic(3, "Аккаунт", account == null ? "Не выбран · будет гостевой режим" : account.Username,
                account == null ? DiagnosticState.Warning : DiagnosticState.Ready);

            var loaderPath = await Connector.GetLoaderExecutablePathAsync();
            if (!File.Exists(loaderPath))
                throw new FileNotFoundException("Не найден компонент запуска SS14.Loader. Пересоберите весь solution.", loaderPath);

            try
            {
                using var content = ContentManager.GetSqliteConnection();
            }
            catch (Exception e)
            {
                throw new IOException("Хранилище контента повреждено или недоступно.", e);
            }
            SetDiagnostic(2, "Компоненты", "Loader и база готовы · движок и файлы восстановятся автоматически", DiagnosticState.Ready);

            await Dns.GetHostAddressesAsync(serverUri.Host, _cancelSource.Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cancelSource.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            var http = Locator.Current.GetRequiredService<HttpClient>();
            using var response = await http.GetAsync(UriHelper.GetServerStatusAddress(serverUri),
                HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Сервер ответил кодом {(int)response.StatusCode}.");
            SetDiagnostic(1, "Сеть", "DNS и API сервера доступны", DiagnosticState.Ready);

            DiagnosticSummary = "Все основные проверки пройдены";
            return true;
        }
        catch (OperationCanceledException) when (_cancelSource.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception e)
        {
            _preflightFailed = true;
            var running = FindRunningDiagnostic();
            if (running >= 0)
                SetDiagnostic(running, Diagnostics[running].Title, FriendlyMessage(e), DiagnosticState.Error);
            DiagnosticSummary = FriendlyMessage(e);
            DiagnosticsVisible = true;
            _windowVm.ShowToast(DiagnosticSummary, true);
            ActivityLog.Record("Диагностика", "Проверка запуска не пройдена", DiagnosticSummary, true);
            return false;
        }
        finally
        {
            _preflightRunning = false;
            NotifyPreflightChanged();
        }
    }

    private void AddDiagnostic(string title, string details, DiagnosticState state)
        => Diagnostics.Add(new LaunchDiagnosticItem(title, details, state));

    private void SetDiagnostic(int index, string title, string details, DiagnosticState state)
        => Diagnostics[index] = new LaunchDiagnosticItem(title, details, state);

    private int FindRunningDiagnostic()
    {
        for (var i = 0; i < Diagnostics.Count; i++)
            if (Diagnostics[i].State == DiagnosticState.Running)
                return i;
        return -1;
    }

    private void NotifyPreflightChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsErrored));
        OnPropertyChanged(nameof(ProgressIndeterminate));
        OnPropertyChanged(nameof(ProgressBarVisible));
        OnPropertyChanged(nameof(SmartStageText));
    }

    public void ToggleDiagnostics() => DiagnosticsVisible = !DiagnosticsVisible;

    public async void RepairAndRetry()
    {
        if (string.IsNullOrWhiteSpace(TargetAddress) || _preflightRunning)
            return;

        try
        {
            LauncherPaths.CreateDirs();
            using var content = ContentManager.GetSqliteConnection();
            DiagnosticSummary = "Рабочие каталоги и хранилище восстановлены · повторная проверка…";
        }
        catch (Exception e)
        {
            DiagnosticSummary = $"Автовосстановление не удалось: {FriendlyMessage(e)}";
            return;
        }

        _errorToastShown = false;
        if (await RunPreflightAsync(TargetAddress))
            _connector.Connect(TargetAddress, _cancelSource.Token);
    }

    public void OpenLogs()
    {
        LauncherPaths.CreateDirs();
        Process.Start(new ProcessStartInfo
        {
            FileName = LauncherPaths.DirLogs,
            UseShellExecute = true
        });
    }

    private static string FriendlyMessage(Exception error) => error switch
    {
        FileNotFoundException => "Отсутствует обязательный компонент Loader.",
        SocketException => "Не удалось разрешить адрес сервера через DNS.",
        HttpRequestException => "Сервер недоступен или отклонил проверочный запрос.",
        IOException => error.Message,
        _ => error.Message
    };

    private static string ClassifyError(Exception? error) => error switch
    {
        null => "Подробности записаны в журнал лаунчера.",
        FileNotFoundException e => $"Компонент запуска не найден: {e.FileName}",
        SocketException => "Ошибка DNS: имя сервера не удалось преобразовать в IP-адрес.",
        HttpRequestException => "Сетевая ошибка: сервер, CDN или служба авторизации недоступны.",
        IOException => "Ошибка файлов: проверьте место на диске и права доступа.",
        _ => error.Message
    };

    private void StartContentBundle(IStorageFile file)
    {
        _connector.LaunchContentBundle(file, _cancelSource.Token);
    }

    public void ErrorDismissed()
    {
        CloseOverlay();
    }

    private void CloseOverlay()
    {
        _windowVm.ConnectingVM = null;
    }

    private async void CloseAfterConnected()
    {
        await System.Threading.Tasks.Task.Delay(700);
        if (_windowVm.ConnectingVM == this)
            CloseOverlay();
    }

    public void Cancel()
    {
        _cancelSource.Cancel();
    }

    public void PrivacyPolicyView()
    {
        Helpers.SafeOpenServerUri(_connector.PrivacyPolicyInfo!.Link);
    }

    public void PrivacyPolicyAccept()
    {
        _connector.ConfirmPrivacyPolicy(PrivacyPolicyAcceptResult.Accepted);
    }

    public void PrivacyPolicyDeny()
    {
        _connector.ConfirmPrivacyPolicy(PrivacyPolicyAcceptResult.Denied);
    }

    public enum ConnectionType
    {
        Server,
        ContentBundle
    }
}

public enum DiagnosticState { Running, Ready, Warning, Error }

public sealed class LaunchDiagnosticItem
{
    public string Title { get; }
    public string Details { get; }
    public DiagnosticState State { get; }

    public LaunchDiagnosticItem(string title, string details, DiagnosticState state)
    {
        Title = title;
        Details = details;
        State = state;
    }

    public string Marker => State switch
    {
        DiagnosticState.Ready => "✓",
        DiagnosticState.Warning => "!",
        DiagnosticState.Error => "×",
        _ => "○"
    };
    public string Color => State switch
    {
        DiagnosticState.Ready => "#63C174",
        DiagnosticState.Warning => "#D6A84B",
        DiagnosticState.Error => "#D76464",
        _ => "#8A8A8A"
    };
}
