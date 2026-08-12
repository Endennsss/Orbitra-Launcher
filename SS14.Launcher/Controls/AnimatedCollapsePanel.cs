using System;
using Avalonia;
using Avalonia.Controls;

namespace SS14.Launcher.Controls;

/// <summary>
/// Animates the space occupied by complex content while allowing the child to
/// retain its full desired size. Repeated Measure calls use Avalonia's cached
/// child measurement instead of constraining and reflowing the entire subtree.
/// </summary>
public sealed class AnimatedCollapsePanel : Decorator
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<AnimatedCollapsePanel, double>(nameof(Progress));

    static AnimatedCollapsePanel()
    {
        AffectsMeasure<AnimatedCollapsePanel>(ProgressProperty);
    }

    public AnimatedCollapsePanel()
    {
        ClipToBounds = true;
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child == null)
            return default;

        Child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        var desired = Child.DesiredSize;
        return new Size(desired.Width, desired.Height * Math.Clamp(Progress, 0, 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child != null)
            Child.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));

        return finalSize;
    }
}
