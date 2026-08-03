using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Splat;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

/// <summary>One process-wide scheduler, so parallel downloads share the configured bandwidth.</summary>
public static class DownloadBandwidthLimiter
{
    private static readonly object Sync = new();
    private static long _nextAllowedTimestamp;

    public static async ValueTask ThrottleAsync(int bytes, CancellationToken cancel)
    {
        int limit;
        try { limit = Locator.Current.GetRequiredService<DataManager>().GetCVar(CVars.DownloadSpeedLimitKib); }
        catch { return; }
        if (limit <= 0 || bytes <= 0)
        {
            lock (Sync) _nextAllowedTimestamp = 0;
            return;
        }

        var now = Stopwatch.GetTimestamp();
        long waitTicks;
        lock (Sync)
        {
            var start = Math.Max(now, _nextAllowedTimestamp);
            waitTicks = start - now;
            var duration = (long)Math.Ceiling(bytes * (double)Stopwatch.Frequency / (limit * 1024d));
            _nextAllowedTimestamp = start + duration;
        }
        if (waitTicks > 0)
            await Task.Delay(TimeSpan.FromSeconds(waitTicks / (double)Stopwatch.Frequency), cancel);
    }
}

public sealed class ThrottledReadStream(Stream inner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        DownloadBandwidthLimiter.ThrottleAsync(read, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        await DownloadBandwidthLimiter.ThrottleAsync(read, cancellationToken);
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        DownloadBandwidthLimiter.ThrottleAsync(read, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return read;
    }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); GC.SuppressFinalize(this); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
