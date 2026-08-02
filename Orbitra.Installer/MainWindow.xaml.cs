using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;

namespace Orbitra.Installer;

public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private enum Page { Setup, Progress, Complete, Error }
    private readonly InstallerService _installer = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Page _page;
    private bool _english;
    private bool _primaryEnabled = true;
    private double _progressValue;
    private string _statusText = "";
    private string _errorText = "";
    private string _installedVersion = "";
    private InstallResult? _result;
    private bool _createDesktopShortcut = true;
    private bool _createStartMenuShortcut = true;

    public MainWindow()
    {
        InstallDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Orbitra Launcher");
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    public InstallerStrings Text => _english ? InstallerStrings.English : InstallerStrings.Russian;
    public string InstallDirectory { get; set; }
    public bool CreateDesktopShortcut { get => _createDesktopShortcut; set => Set(ref _createDesktopShortcut, value); }
    public bool CreateStartMenuShortcut { get => _createStartMenuShortcut; set => Set(ref _createStartMenuShortcut, value); }
    public string DesktopShortcutText => _english ? "Create a desktop shortcut" : "Создать ярлык на рабочем столе";
    public string StartMenuShortcutText => _english ? "Add to the Start menu" : "Добавить в меню «Пуск»";
    public bool IsSetupPage => _page == Page.Setup;
    public bool IsProgressPage => _page == Page.Progress;
    public bool IsCompletePage => _page == Page.Complete;
    public bool IsErrorPage => _page == Page.Error;
    public bool PrimaryEnabled { get => _primaryEnabled; private set => Set(ref _primaryEnabled, value); }
    public double ProgressValue { get => _progressValue; private set { if (Set(ref _progressValue, value)) OnChanged(nameof(ProgressPercent)); } }
    public string ProgressPercent => $"{ProgressValue:P0}";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }
    public string InstalledVersion { get => _installedVersion; private set => Set(ref _installedVersion, value); }
    public string PrimaryButtonText => _page switch
    {
        Page.Setup => Text.Install,
        Page.Progress => Text.Installing,
        Page.Complete => Text.LaunchAndLogin,
        Page.Error => Text.Retry,
        _ => Text.Install
    };

    private async void PrimaryClicked(object? sender, RoutedEventArgs e)
    {
        if (_page == Page.Complete && _result != null)
        {
            InstallerService.Launch(_result.Executable);
            Close();
            return;
        }
        if (_page == Page.Error) { SetPage(Page.Setup); return; }
        if (_page != Page.Setup) return;
        await InstallAsync();
    }

    private async Task InstallAsync()
    {
        SetPage(Page.Progress);
        PrimaryEnabled = false;
        ProgressValue = 0;
        ErrorText = "";
        try
        {
            var progress = new Progress<InstallProgress>(value =>
            {
                ProgressValue = value.Value;
                StatusText = _english ? value.English : value.Russian;
            });
            _result = await _installer.InstallLatestAsync(InstallDirectory, CreateDesktopShortcut,
                CreateStartMenuShortcut, progress, _lifetime.Token);
            InstalledVersion = $"{_result.Version}  ·  {_result.Directory}";
            SetPage(Page.Complete);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
            SetPage(Page.Error);
        }
        finally { PrimaryEnabled = true; }
    }

    private async void BrowseClicked(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Text.InstallPath,
            AllowMultiple = false
        });
        if (folders.Count == 0) return;
        InstallDirectory = Path.Combine(folders[0].Path.LocalPath, "Orbitra Launcher");
        OnChanged(nameof(InstallDirectory));
    }

    private void RussianClicked(object? sender, RoutedEventArgs e) => SetLanguage(false);
    private void EnglishClicked(object? sender, RoutedEventArgs e) => SetLanguage(true);
    private void SetLanguage(bool english)
    {
        _english = english;
        OnChanged(nameof(Text));
        OnChanged(nameof(PrimaryButtonText));
        OnChanged(nameof(DesktopShortcutText));
        OnChanged(nameof(StartMenuShortcutText));
        if (_page == Page.Progress && string.IsNullOrWhiteSpace(StatusText)) StatusText = Text.Installing;
    }

    private void SetPage(Page page)
    {
        _page = page;
        OnChanged(nameof(IsSetupPage)); OnChanged(nameof(IsProgressPage));
        OnChanged(nameof(IsCompletePage)); OnChanged(nameof(IsErrorPage));
        OnChanged(nameof(PrimaryButtonText));
    }

    private void TitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    private void MinimizeClicked(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
    protected override void OnClosed(EventArgs e) { _lifetime.Cancel(); _installer.Dispose(); base.OnClosed(e); }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (Equals(field, value)) return false;
        field = value; OnChanged(property); return true;
    }
    private void OnChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
