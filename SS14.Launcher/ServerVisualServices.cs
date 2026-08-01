using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Serilog;
using Splat;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

public static class ServerIconCache
{
    private static readonly HttpClient Http = Locator.Current.GetRequiredService<HttpClient>();
    private static readonly string CacheDirectory = Path.Combine(LauncherPaths.DirLocalData, "server-icons");

    public static async Task<Bitmap?> GetAsync(string address)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address)));
            var path = Path.Combine(CacheDirectory, hash + ".img");

            if (!File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromDays(7))
            {
                var server = UriHelper.ParseSs14Uri(address);
                var iconUri = new Uri(UriHelper.GetServerApiAddress(server), "icon");
                using var response = await Http.GetAsync(iconUri);
                if (!response.IsSuccessStatusCode)
                    return null;
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length is 0 or > 2_000_000)
                    return null;
                await File.WriteAllBytesAsync(path, bytes);
            }

            await using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Unable to load server icon for {Address}", address);
            return null;
        }
    }
}

/// <summary>
/// Small non-activating launcher-owned popup shown above other windows.
/// This intentionally does not use the Windows notification center.
/// </summary>
public static class SystemNotificationService
{
    private const double PopupWidth = 500;
    private const double PopupHeight = 146;
    private const int ScreenMargin = 18;
    private const int PopupGap = 10;
    private static readonly List<Window> ActiveWindows = [];

    public static void Show(string title, string message, Action? connect = null, Action? open = null, Action? disable = null)
    {
        Dispatcher.UIThread.Post(() => ShowCore(title, message, connect, open, disable));
    }

    private static async void ShowCore(string title, string message, Action? connect, Action? open, Action? disable)
    {
        UiSoundService.PlayNotification();
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var messageBlock = new TextBlock
        {
            Text = message,
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#B8B8B8")),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
        };

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("4,*,Auto"),
            Background = new SolidColorBrush(Color.Parse("#F2181818")),
        };
        var accent = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#E8E8E8")),
        };
        Grid.SetColumn(accent, 0);
        content.Children.Add(accent);
        var text = new StackPanel
        {
            Spacing = 5,
            Margin = new Thickness(16, 13, 12, 12),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleBlock, messageBlock },
        };
        if (connect != null || open != null || disable != null)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            void AddAction(string caption, Action? action)
            {
                if (action == null) return;
                var button = new Button { Content = caption, Padding = new Thickness(9, 5), FontSize = 11 };
                button.Click += (_, _) => { action(); };
                actions.Children.Add(button);
            }
            AddAction("Подключиться", connect);
            AddAction("Открыть сервер", open);
            AddAction("Больше не уведомлять", disable);
            text.Children.Add(actions);
        }
        Grid.SetColumn(text, 1);
        content.Children.Add(text);
        var close = new Button
        {
            Content = "×",
            Width = 34,
            Height = 34,
            Margin = new Thickness(0, 8, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#A0A0A0")),
        };
        Grid.SetColumn(close, 2);
        content.Children.Add(close);

        var window = new Window
        {
            Width = PopupWidth,
            Height = PopupHeight,
            CanResize = false,
            SystemDecorations = SystemDecorations.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Background = Brushes.Transparent,
            Content = new Border
            {
                BorderBrush = new SolidColorBrush(Color.Parse("#505050")),
                BorderThickness = new Thickness(1),
                BoxShadow = BoxShadows.Parse("0 8 24 #99000000"),
                Child = content,
            },
            Opacity = 0,
        };

        close.Click += (_, _) => window.Close();
        window.Closed += (_, _) =>
        {
            ActiveWindows.Remove(window);
            RepositionAll();
        };
        window.Opened += (_, _) => PositionWindow(window, ActiveWindows.IndexOf(window));

        ActiveWindows.Add(window);
        window.Show();

        for (var opacity = 0.0; opacity <= 1; opacity += 0.14)
        {
            if (!window.IsVisible)
                return;
            window.Opacity = opacity;
            await Task.Delay(18);
        }
        window.Opacity = 1;

        await Task.Delay(4200);
        for (var opacity = 1.0; opacity >= 0; opacity -= 0.08)
        {
            if (!window.IsVisible)
                return;
            window.Opacity = opacity;
            await Task.Delay(25);
        }

        if (window.IsVisible)
            window.Close();
    }

    private static void RepositionAll()
    {
        for (var i = 0; i < ActiveWindows.Count; i++)
            PositionWindow(ActiveWindows[i], i);
    }

    private static void PositionWindow(Window window, int index)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null)
            return;

        var scale = screen.Scaling;
        var area = screen.WorkingArea;
        var width = (int)Math.Ceiling(PopupWidth * scale);
        var height = (int)Math.Ceiling(PopupHeight * scale);
        window.Position = new PixelPoint(
            area.Right - width - ScreenMargin,
            area.Bottom - height - ScreenMargin - index * (height + PopupGap));
    }
}
