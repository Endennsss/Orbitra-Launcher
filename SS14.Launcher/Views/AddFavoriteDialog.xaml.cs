using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;

namespace SS14.Launcher.Views;

public partial class AddFavoriteDialog : Window
{
    public AddFavoriteDialog()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        NameBox.Focus();
    }

    private void TrySubmit(object? _1, RoutedEventArgs _2)
    {
        Close((NameBox.Text?.Trim() ?? "", AddressBox.Text?.Trim() ?? ""));
    }

    private void UpdateSubmitValid(object? _1, TextChangedEventArgs _2)
    {
        var validAddr = DirectConnectDialog.IsAddressValid(AddressBox.Text);
        var valid = validAddr && !string.IsNullOrEmpty(NameBox.Text);

        SubmitButton.IsEnabled = valid;
        TxtInvalid.IsVisible = !validAddr;
    }
    private void TitleBarPressed(object? sender, PointerPressedEventArgs e) { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); }
    private void MinimizeClicked(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseWindowClicked(object? sender, RoutedEventArgs e) => Close();
}
