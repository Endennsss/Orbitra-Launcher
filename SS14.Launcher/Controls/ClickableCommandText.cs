using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace SS14.Launcher.Controls;

/// <summary>
/// A lightweight clickable content element for text actions that must not use
/// the visual or interaction template of a button.
/// </summary>
public sealed class ClickableCommandText : ContentControl
{
    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<ClickableCommandText, ICommand?>(nameof(Command));

    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (e.InitialPressMouseButton != MouseButton.Left || !IsPointerOver)
            return;

        var command = Command;
        if (command?.CanExecute(null) != true)
            return;

        command.Execute(null);
        e.Handled = true;
    }
}
