using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace SS14.Launcher.Views;

public partial class ThemeWorkshopWindow : Window
{
    public ThemeWorkshopWindow() => AvaloniaXamlLoader.Load(this);

    private void TitleBarPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(args);
    }

    private void MinimizeClicked(object? sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;
    private void CloseClicked(object? sender, RoutedEventArgs args) => Close();
}
