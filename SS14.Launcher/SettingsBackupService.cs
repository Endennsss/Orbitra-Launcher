using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SS14.Launcher.Models.Data;

namespace SS14.Launcher;

public static class SettingsBackupService
{
    public static void Export(string archivePath, DataManager cfg)
    {
        var values = new Dictionary<string, object?>
        {
            [CVars.LauncherFont.Name] = cfg.GetCVar(CVars.LauncherFont), [CVars.LightTheme.Name] = cfg.GetCVar(CVars.LightTheme),
            [CVars.CustomThemeEnabled.Name] = cfg.GetCVar(CVars.CustomThemeEnabled), [CVars.CustomThemeBackground.Name] = cfg.GetCVar(CVars.CustomThemeBackground),
            [CVars.CustomThemeSurface.Name] = cfg.GetCVar(CVars.CustomThemeSurface), [CVars.CustomThemeControl.Name] = cfg.GetCVar(CVars.CustomThemeControl),
            [CVars.CustomThemeAccent.Name] = cfg.GetCVar(CVars.CustomThemeAccent), [CVars.CustomThemeText.Name] = cfg.GetCVar(CVars.CustomThemeText),
            [CVars.CustomThemeMuted.Name] = cfg.GetCVar(CVars.CustomThemeMuted), [CVars.CustomThemeBlur.Name] = cfg.GetCVar(CVars.CustomThemeBlur),
            [CVars.UiSoundsEnabled.Name] = cfg.GetCVar(CVars.UiSoundsEnabled), [CVars.UiSoundVolume.Name] = cfg.GetCVar(CVars.UiSoundVolume),
            [CVars.CloseToTray.Name] = cfg.GetCVar(CVars.CloseToTray), [CVars.NavigationTabOrder.Name] = cfg.GetCVar(CVars.NavigationTabOrder),
            [CVars.HiddenNavigationTabs.Name] = cfg.GetCVar(CVars.HiddenNavigationTabs), [CVars.DiscordRpcEnabled.Name] = cfg.GetCVar(CVars.DiscordRpcEnabled),
            [CVars.FavoriteNotificationsEnabled.Name] = cfg.GetCVar(CVars.FavoriteNotificationsEnabled)
        };
        if (File.Exists(archivePath)) File.Delete(archivePath);
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("settings.json", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(entry.Open())) writer.Write(JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        AddBackground(archive, cfg.GetCVar(CVars.CustomThemeImage), "background");
    }

    public static void Import(string archivePath, DataManager cfg)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry("settings.json") ?? throw new InvalidDataException("settings.json missing");
        using var document = JsonDocument.Parse(entry.Open());
        var root = document.RootElement;
        SetString(CVars.LauncherFont); SetBool(CVars.LightTheme); SetBool(CVars.CustomThemeEnabled);
        SetString(CVars.CustomThemeBackground); SetString(CVars.CustomThemeSurface); SetString(CVars.CustomThemeControl);
        SetString(CVars.CustomThemeAccent); SetString(CVars.CustomThemeText); SetString(CVars.CustomThemeMuted); SetInt(CVars.CustomThemeBlur);
        SetBool(CVars.UiSoundsEnabled); SetInt(CVars.UiSoundVolume); SetBool(CVars.CloseToTray);
        SetString(CVars.NavigationTabOrder); SetString(CVars.HiddenNavigationTabs); SetBool(CVars.DiscordRpcEnabled); SetBool(CVars.FavoriteNotificationsEnabled);
        var background = archive.Entries.Find(e => e.FullName.StartsWith("background.", StringComparison.OrdinalIgnoreCase));
        if (background != null)
        {
            var target = Path.Combine(LauncherPaths.DirUserData, "custom-theme-background" + Path.GetExtension(background.Name));
            background.ExtractToFile(target, true); cfg.SetCVar(CVars.CustomThemeImage, target);
        }
        cfg.CommitConfig();
        void SetString(CVarDef<string> c) { if (root.TryGetProperty(c.Name, out var v)) cfg.SetCVar(c, v.GetString() ?? c.DefaultValue); }
        void SetBool(CVarDef<bool> c) { if (root.TryGetProperty(c.Name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False) cfg.SetCVar(c, v.GetBoolean()); }
        void SetInt(CVarDef<int> c) { if (root.TryGetProperty(c.Name, out var v) && v.TryGetInt32(out var value)) cfg.SetCVar(c, value); }
    }

    private static void AddBackground(ZipArchive archive, string path, string name)
    {
        if (File.Exists(path)) archive.CreateEntryFromFile(path, name + Path.GetExtension(path), CompressionLevel.Optimal);
    }

    private static ZipArchiveEntry? Find(this IReadOnlyCollection<ZipArchiveEntry> entries, Func<ZipArchiveEntry, bool> predicate)
    { foreach (var entry in entries) if (predicate(entry)) return entry; return null; }
}
