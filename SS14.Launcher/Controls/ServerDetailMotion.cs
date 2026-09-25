using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SS14.Launcher.Controls;

/// <summary>Replays a short section reveal on selection changes without rebuilding the detail view.</summary>
public sealed class ServerDetailMotion : AvaloniaObject
{
    public static readonly AttachedProperty<int> DelayProperty =
        AvaloniaProperty.RegisterAttached<ServerDetailMotion, Control, int>("Delay", -1);
    private static readonly ConditionalWeakTable<Control, MotionState> States = new();

    static ServerDetailMotion()
    {
        DelayProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (GetDelay(control) >= 0)
                States.GetValue(control, c => new MotionState(c)).Request();
            else if (States.TryGetValue(control, out var state))
            {
                state.Dispose();
                States.Remove(control);
            }
        });
    }

    public static int GetDelay(Control control) => control.GetValue(DelayProperty);
    public static void SetDelay(Control control, int value) => control.SetValue(DelayProperty, value);

    private sealed class MotionState : IDisposable
    {
        private readonly Control _control;
        private CancellationTokenSource? _animation;
        private bool _queued;
        private readonly List<Visual> _ancestors = new();

        public MotionState(Control control)
        {
            _control = control;
            control.DataContextChanged += Changed;
            control.AttachedToVisualTree += Attached;
            control.DetachedFromVisualTree += Detached;
            control.PropertyChanged += PropertyChanged;
            ObserveAncestors();
        }

        private void Changed(object? sender, EventArgs e) => Request();
        private void Attached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            ObserveAncestors();
            Request();
        }
        private void Detached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            Cancel();
            ClearAncestors();
        }
        private void ObserveAncestors()
        {
            ClearAncestors();
            foreach (var ancestor in _control.GetVisualAncestors())
            {
                _ancestors.Add(ancestor);
                ancestor.PropertyChanged += PropertyChanged;
            }
        }
        private void ClearAncestors()
        {
            foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= PropertyChanged;
            _ancestors.Clear();
        }
        private void PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Visual.IsVisibleProperty)
            {
                if (_control.IsEffectivelyVisible) Request(); else Cancel();
            }
        }

        public void Request()
        {
            if (_queued) return;
            _queued = true;
            Dispatcher.UIThread.Post(Play, DispatcherPriority.Loaded);
        }

        private async void Play()
        {
            _queued = false;
            Cancel();
            if (_control.GetVisualRoot() == null || !_control.IsEffectivelyVisible || _control.DataContext == null || GetDelay(_control) < 0)
                return;
            var cancellation = new CancellationTokenSource();
            _animation = cancellation;
            var animation = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(240),
                Delay = TimeSpan.FromMilliseconds(Math.Clamp(GetDelay(_control), 0, 80)),
                FillMode = FillMode.Backward,
                Easing = new CubicEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.35) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } }
                }
            };
            try { await animation.RunAsync(_control, cancellation.Token); }
            catch (OperationCanceledException) { }
            finally
            {
                if (ReferenceEquals(_animation, cancellation)) _animation = null;
                cancellation.Dispose();
            }
        }

        private void Cancel()
        {
            _animation?.Cancel();
            _animation = null;
        }

        public void Dispose()
        {
            Cancel();
            ClearAncestors();
            _control.DataContextChanged -= Changed;
            _control.AttachedToVisualTree -= Attached;
            _control.DetachedFromVisualTree -= Detached;
            _control.PropertyChanged -= PropertyChanged;
        }
    }
}
