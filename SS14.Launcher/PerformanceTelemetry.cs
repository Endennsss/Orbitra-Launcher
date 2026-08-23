using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SS14.Launcher;

/// <summary>
/// Lightweight in-process telemetry used by the local performance center.
/// No values leave the launcher.
/// </summary>
public static class PerformanceTelemetry
{
    private static readonly object Lock = new();
    private static readonly Stopwatch Lifetime = Stopwatch.StartNew();
    private static readonly List<PerformanceOperation> Operations = [];
    private static TimeSpan? _uiReady;

    public static TimeSpan Uptime => Lifetime.Elapsed;
    public static TimeSpan? UiReady => _uiReady;

    public static void MarkProcessStart()
    {
        // Touching the type starts the process lifetime stopwatch as early as possible.
        _ = Lifetime.Elapsed;
    }

    public static void MarkUiReady() => _uiReady ??= Lifetime.Elapsed;

    public static IDisposable Measure(string name, string? details = null) =>
        new Measurement(name, details);

    public static void Record(string name, TimeSpan elapsed, string? details = null)
    {
        lock (Lock)
        {
            Operations.Add(new PerformanceOperation(name, elapsed, DateTimeOffset.Now, details ?? string.Empty));
            if (Operations.Count > 160)
                Operations.RemoveRange(0, Operations.Count - 160);
        }
    }

    public static IReadOnlyList<PerformanceOperation> Snapshot()
    {
        lock (Lock)
            return Operations.ToArray();
    }

    public static PerformanceOperation? Latest(string name)
    {
        lock (Lock)
            return Operations.LastOrDefault(x => x.Name.Equals(name, StringComparison.Ordinal));
    }

    private sealed class Measurement(string name, string? details) : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _stopwatch.Stop();
            Record(name, _stopwatch.Elapsed, details);
        }
    }
}

public sealed record PerformanceOperation(
    string Name,
    TimeSpan Elapsed,
    DateTimeOffset Timestamp,
    string Details);
