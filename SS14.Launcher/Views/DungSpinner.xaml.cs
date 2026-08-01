using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Media;

namespace SS14.Launcher.Views;

/// <summary>
/// Animated spinner doodad
/// </summary>
/// <remarks>
/// Because of limitations in Avalonia, all usage sites MUST set IsVisible to false when the control is not visible.
/// Otherwise, there will be significant idle resource usage in the launcher.
/// </remarks>
[PseudoClasses("active")]
public sealed partial class DungSpinner : UserControl
{
    public static readonly StyledProperty<double> AnimationProgressProperty =
        AvaloniaProperty.Register<DungSpinner, double>(nameof(AnimationProgress));

    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<DungSpinner, IBrush>(nameof(Fill));

    static DungSpinner()
    {
        AffectsRender<DungSpinner>(AnimationProgressProperty, FillProperty);
    }

    public DungSpinner()
    {
        InitializeComponent();

        UpdatePseudoClass();
    }

    public double AnimationProgress
    {
        get => GetValue(AnimationProgressProperty);
        set => SetValue(AnimationProgressProperty, value);
    }

    public IBrush Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            UpdatePseudoClass();
        }
    }

    private void UpdatePseudoClass()
    {
        PseudoClasses.Set(":active", IsVisible);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Console.WriteLine($"RENDER: {IsEffectivelyVisible}");

        var centerX = Bounds.Width / 2;
        var centerY = Bounds.Height / 2;

        // Offset so that 0,0 is the center of the control.
        var offset = Matrix.CreateTranslation(centerX, centerY);

        using var translateState = context.PushTransform(offset);

        const int dots = 12;
        var orbit = Math.Max(5, Math.Min(Bounds.Width, Bounds.Height) * 0.32);
        var dotRadius = Math.Max(1.2, orbit * 0.12);
        var active = (int)(AnimationProgress * dots) % dots;

        for (var i = 0; i < dots; i++)
        {
            var distance = (i - active + dots) % dots;
            var alpha = (byte)Math.Clamp(245 - distance * 17, 45, 245);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, 242, 242, 242));
            var angle = i * Math.PI * 2 / dots - Math.PI / 2;
            var x = Math.Cos(angle) * orbit;
            var y = Math.Sin(angle) * orbit;
            var radius = distance == 0 ? dotRadius * 1.25 : dotRadius;
            context.DrawEllipse(brush, null, new Point(x, y), radius, radius);
        }
    }
}
