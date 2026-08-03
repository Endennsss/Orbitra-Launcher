using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SS14.Launcher;

/// <summary>Renders an invisible sample launcher page into a PNG for workshop cards.</summary>
public static class ThemePreviewRenderer
{
    public static byte[] Render(string background, string surface, string control, string accent,
        string text, string muted, int blur, int dimming, string? imagePath)
    {
        var root = new Grid { Width = 800, Height = 450, Background = Brush(background) };
        Bitmap? image = null;
        try
        {
            if (File.Exists(imagePath))
            {
                image = new Bitmap(imagePath);
                var backgroundImage = new Image { Source = image, Stretch = Stretch.UniformToFill };
                if (blur > 0) backgroundImage.Effect = new BlurEffect { Radius = Math.Clamp(blur, 0, 40) };
                root.Children.Add(backgroundImage);
                root.Children.Add(new Border { Background = Brushes.Black, Opacity = Math.Clamp(dimming, 0, 90) / 100d });
            }

            var chrome = new Border
            {
                Height = 42, VerticalAlignment = VerticalAlignment.Top, Background = Brush(surface),
                BorderBrush = Brush(control), BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = "ORBITRA LAUNCHER · THEME PREVIEW", Foreground = Brush(muted),
                    FontSize = 12, FontWeight = FontWeight.Bold, LetterSpacing = 1.2,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0)
                }
            };
            root.Children.Add(chrome);

            var content = new Grid
            {
                Margin = new Thickness(28, 68, 28, 24),
                ColumnDefinitions = new ColumnDefinitions("210,18,*")
            };
            var navigation = new Border { Background = Brush(surface), BorderBrush = Brush(control), BorderThickness = new Thickness(1), Padding = new Thickness(14) };
            var navigationItems = new StackPanel { Spacing = 8 };
            navigationItems.Children.Add(Label("ORBITRA CONTROL", muted, 11, FontWeight.Bold));
            foreach (var (name, selected) in new[] { ("Главная", true), ("Серверы", false), ("Новости", false), ("Кастом тема", false) })
                navigationItems.Children.Add(new Border
                {
                    Background = selected ? Brush(control) : Brushes.Transparent, Padding = new Thickness(12, 9),
                    Child = Label(name, text, 15, FontWeight.SemiBold)
                });
            navigation.Child = navigationItems;
            content.Children.Add(navigation);

            var page = new StackPanel { Spacing = 12 };
            Grid.SetColumn(page, 2);
            page.Children.Add(Label("OVERVIEW", muted, 11, FontWeight.Bold));
            page.Children.Add(Label("Избранные серверы", text, 28, FontWeight.Bold));
            foreach (var index in new[] { 1, 2, 3 })
            {
                var card = new Grid { ColumnDefinitions = new ColumnDefinitions("8,*,Auto"), Background = Brush(surface), Height = 68 };
                card.Children.Add(new Border { Background = Brush(accent) });
                var info = new StackPanel { Margin = new Thickness(16, 9), Spacing = 3 };
                Grid.SetColumn(info, 1);
                info.Children.Add(Label(index == 1 ? "ORBITRA STATION" : $"SPACE STATION {index}", text, 16, FontWeight.SemiBold));
                info.Children.Add(Label(index == 1 ? "Online · 48 / 120 · 54 ms" : "Online · сервер доступен", muted, 12));
                card.Children.Add(info);
                var button = new Border { Background = Brush(control), Margin = new Thickness(8), Padding = new Thickness(14, 8), Child = Label("Подключиться", text, 13, FontWeight.SemiBold) };
                Grid.SetColumn(button, 2);
                card.Children.Add(button);
                page.Children.Add(card);
            }
            content.Children.Add(page);
            root.Children.Add(content);

            root.Measure(new Size(800, 450));
            root.Arrange(new Rect(0, 0, 800, 450));
            using var rendered = new RenderTargetBitmap(new PixelSize(800, 450), new Vector(96, 96));
            rendered.Render(root);
            using var output = new MemoryStream();
            rendered.Save(output);
            return output.ToArray();
        }
        finally { image?.Dispose(); }
    }

    private static TextBlock Label(string value, string color, double size, FontWeight? weight = null) => new()
    {
        Text = value, Foreground = Brush(color), FontSize = size, FontWeight = weight ?? FontWeight.Normal,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static IBrush Brush(string value)
    {
        try { return new SolidColorBrush(Color.Parse(value)); }
        catch { return Brushes.Black; }
    }
}
