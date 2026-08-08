using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Linq;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

public static class PlaytimeTracker
{
    private static readonly object Sync = new();
    private static readonly string FilePath = Path.Combine(LauncherPaths.DirUserData, "server-playtime.json");
    private static readonly Dictionary<string, long> Seconds = Load();
    private static readonly string MetaFilePath = Path.Combine(LauncherPaths.DirUserData, "server-playtime-meta.json");
    private static readonly Dictionary<string, PlaytimeMeta> Meta = LoadMeta();
    private static string? _activeAddress;
    private static DateTime _startedAt;
    private static readonly Timer SaveTimer = new(_ => Save(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    public static event Action<string>? Changed;

    public static void Start(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        lock (Sync)
        {
            StopCore();
            _activeAddress = address;
            _startedAt = DateTime.UtcNow;
            Meta.TryGetValue(address, out var oldMeta);
            Meta[address] = new PlaytimeMeta(oldMeta?.Name ?? address, DateTimeOffset.Now);
        }
        Changed?.Invoke(address);
    }

    public static void Stop()
    {
        string? address;
        lock (Sync) { address = _activeAddress; StopCore(); SaveCore(); }
        if (address != null) Changed?.Invoke(address);
    }

    public static TimeSpan Get(string address)
    {
        lock (Sync)
        {
            Seconds.TryGetValue(address, out var seconds);
            if (string.Equals(_activeAddress, address, StringComparison.OrdinalIgnoreCase))
                seconds += Math.Max(0, (long)(DateTime.UtcNow - _startedAt).TotalSeconds);
            return TimeSpan.FromSeconds(seconds);
        }
    }

    public static void SetServerName(string address, string name)
    {
        lock (Sync)
        {
            Meta.TryGetValue(address, out var oldMeta);
            Meta[address] = new PlaytimeMeta(string.IsNullOrWhiteSpace(name) ? address : name,
                oldMeta?.LastPlayed ?? DateTimeOffset.MinValue);
            SaveCore();
        }
    }

    public static IReadOnlyList<PlaytimeServerEntry> GetAll()
    {
        lock (Sync)
        {
            return Seconds.Keys.Concat(Meta.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(address =>
                {
                    Meta.TryGetValue(address, out var meta);
                    return new PlaytimeServerEntry(address, meta?.Name ?? address, Get(address), meta?.LastPlayed);
                }).OrderByDescending(x => x.Duration).ToArray();
        }
    }

    public static void Clear()
    {
        string? activeAddress;
        lock (Sync)
        {
            activeAddress = _activeAddress;
            Seconds.Clear();
            Meta.Clear();
            if (activeAddress != null)
                _startedAt = DateTime.UtcNow;
            SaveCore();
        }
        Changed?.Invoke(activeAddress ?? string.Empty);
    }

    private static void StopCore()
    {
        if (_activeAddress == null) return;
        Seconds.TryGetValue(_activeAddress, out var seconds);
        Seconds[_activeAddress] = seconds + Math.Max(0, (long)(DateTime.UtcNow - _startedAt).TotalSeconds);
        _activeAddress = null;
    }

    private static void Save()
    {
        string? address;
        lock (Sync) { address = _activeAddress; SaveCore(); }
        if (address != null) Changed?.Invoke(address);
    }
    private static void SaveCore()
    {
        try
        {
            var snapshot = new Dictionary<string, long>(Seconds, StringComparer.OrdinalIgnoreCase);
            if (_activeAddress != null)
            {
                snapshot.TryGetValue(_activeAddress, out var seconds);
                snapshot[_activeAddress] = seconds + Math.Max(0, (long)(DateTime.UtcNow - _startedAt).TotalSeconds);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot));
            File.WriteAllText(MetaFilePath, JsonSerializer.Serialize(Meta));
        }
        catch { }
    }

    private static Dictionary<string, long> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(FilePath))
                       ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, PlaytimeMeta> LoadMeta()
    {
        try { return File.Exists(MetaFilePath)
            ? JsonSerializer.Deserialize<Dictionary<string, PlaytimeMeta>>(File.ReadAllText(MetaFilePath)) ?? new(StringComparer.OrdinalIgnoreCase)
            : new(StringComparer.OrdinalIgnoreCase); }
        catch { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private sealed record PlaytimeMeta(string Name, DateTimeOffset LastPlayed);
}

public sealed record PlaytimeServerEntry(string Address, string Name, TimeSpan Duration, DateTimeOffset? LastPlayed);
