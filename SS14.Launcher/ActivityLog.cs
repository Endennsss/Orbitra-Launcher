using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

public static class ActivityLog
{
    private static readonly object Sync = new();
    private static readonly string FilePath = Path.Combine(LauncherPaths.DirUserData, "activity-log.json");
    private static List<ActivityEntry> _entries = Load();
    public static event Action? Changed;

    public static IReadOnlyList<ActivityEntry> GetEntries()
    {
        lock (Sync) return _entries.OrderByDescending(x => x.Time).ToArray();
    }

    public static void Record(string category, string title, string details, bool error = false)
    {
        lock (Sync)
        {
            _entries.Add(new ActivityEntry(DateTimeOffset.Now, category, title, details, error));
            if (_entries.Count > 300) _entries = _entries.TakeLast(300).ToList();
            Save();
        }
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Sync) { _entries.Clear(); Save(); }
        Changed?.Invoke();
    }

    private static List<ActivityEntry> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<ActivityEntry>>(File.ReadAllText(FilePath)) ?? [] : []; }
        catch { return []; }
    }
    private static void Save()
    {
        try { Directory.CreateDirectory(LauncherPaths.DirUserData); File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries)); }
        catch { }
    }
}

public sealed record ActivityEntry(DateTimeOffset Time, string Category, string Title, string Details, bool IsError);
