using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace SS14.Launcher.Views;
public partial class ProfileSettingsWindow : Window
{
    public ProfileSettingsWindow() => InitializeComponent();
    private void TitleBarPressed(object? sender,PointerPressedEventArgs e){if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)BeginMoveDrag(e);}
    private void MinimizeClicked(object? sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaximizeClicked(object? sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void CloseClicked(object? sender, RoutedEventArgs e) => Close();
}
