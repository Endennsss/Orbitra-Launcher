using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Toolkit.Mvvm.Input;
using YamlDotNet.RepresentationModel;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed partial class LocalServersTabViewModel
{
    private readonly string _configHistoryRoot = Path.Combine(LauncherPaths.DirUserData, "LocalServerConfigHistory");
    private readonly Dictionary<string, string> _configBaselines = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<LocalConfigDiffLineViewModel> ConfigDiffLines { get; } = [];
    public ObservableCollection<LocalConfigHistoryEntryViewModel> ConfigHistory { get; } = [];

    private bool _isSimpleConfigMode = true;
    public bool IsSimpleConfigMode
    {
        get => _isSimpleConfigMode;
        set { if (SetProperty(ref _isSimpleConfigMode, value)) OnPropertyChanged(nameof(IsAdvancedConfigMode)); }
    }
    public bool IsAdvancedConfigMode => !IsSimpleConfigMode;

    private string _simpleServerName = string.Empty;
    public string SimpleServerName { get => _simpleServerName; set => SetProperty(ref _simpleServerName, value); }
    private int _simpleMaxPlayers = 50;
    public int SimpleMaxPlayers { get => _simpleMaxPlayers; set => SetProperty(ref _simpleMaxPlayers, Math.Clamp(value, 1, 1000)); }
    private string _simpleMap = string.Empty;
    public string SimpleMap { get => _simpleMap; set => SetProperty(ref _simpleMap, value); }
    private string _simpleGamePreset = string.Empty;
    public string SimpleGamePreset { get => _simpleGamePreset; set => SetProperty(ref _simpleGamePreset, value); }

    private string _configValidationText = "Загрузите конфигурацию для проверки.";
    public string ConfigValidationText { get => _configValidationText; private set => SetProperty(ref _configValidationText, value); }
    private bool _hasConfigErrors;
    public bool HasConfigErrors { get => _hasConfigErrors; private set => SetProperty(ref _hasConfigErrors, value); }
    private int _configErrorLine;
    public int ConfigErrorLine { get => _configErrorLine; private set { if (SetProperty(ref _configErrorLine, value)) OnPropertyChanged(nameof(ConfigErrorLineText)); } }
    public string ConfigErrorLineText => ConfigErrorLine > 0 ? $"Строка {ConfigErrorLine}" : string.Empty;
    private string _configDiffSummary = "Нет загруженной сохранённой версии.";
    public string ConfigDiffSummary { get => _configDiffSummary; private set => SetProperty(ref _configDiffSummary, value); }
    public bool HasConfigDiff => ConfigDiffLines.Count > 0;

    private LocalConfigHistoryEntryViewModel? _selectedConfigHistoryEntry;
    public LocalConfigHistoryEntryViewModel? SelectedConfigHistoryEntry
    {
        get => _selectedConfigHistoryEntry;
        set
        {
            if (!SetProperty(ref _selectedConfigHistoryEntry, value)) return;
            RestoreConfigVersionCommand?.NotifyCanExecuteChanged();
        }
    }

    public IRelayCommand<string> SetConfigEditorModeCommand { get; private set; } = null!;
    public IRelayCommand ApplySimpleConfigCommand { get; private set; } = null!;
    public IRelayCommand ValidateConfigCommand { get; private set; } = null!;
    public IRelayCommand RestoreConfigVersionCommand { get; private set; } = null!;
    public IRelayCommand RestoreLastWorkingConfigCommand { get; private set; } = null!;

    private void InitializeConfigurationFeatures()
    {
        Directory.CreateDirectory(_configHistoryRoot);
        SetConfigEditorModeCommand = new RelayCommand<string>(mode => IsSimpleConfigMode = !string.Equals(mode, "advanced", StringComparison.OrdinalIgnoreCase));
        ApplySimpleConfigCommand = new RelayCommand(ApplySimpleConfiguration, () => HasSelection && !CanStop);
        ValidateConfigCommand = new RelayCommand(() => ValidateCurrentConfig(true), () => HasSelection);
        RestoreConfigVersionCommand = new RelayCommand(RestoreSelectedConfigVersion, () => SelectedConfigHistoryEntry != null && !CanStop);
        RestoreLastWorkingConfigCommand = new RelayCommand(RestoreLastWorkingConfig, () => HasLastWorkingConfig() && !CanStop);
    }

    private string ConfigKey(LocalServerProfileViewModel server) => server.Id + "|" + server.ConfigFile;

    private void OnConfigurationSelectionChanged(LocalServerProfileViewModel? server)
    {
        ConfigDiffLines.Clear();
        LoadConfigHistory();
        if (server == null || string.IsNullOrWhiteSpace(server.ConfigFile))
        {
            ConfigValidationText = "Основная конфигурация не выбрана.";
            HasConfigErrors = false;
            return;
        }
        try
        {
            var path = ResolveInsideServer(server, server.ConfigFile);
            if (File.Exists(path))
            {
                server.ConfigText = File.ReadAllText(path);
                OnConfigLoaded(server);
            }
        }
        catch (Exception e)
        {
            HasConfigErrors = true;
            ConfigValidationText = e.Message;
        }
        NotifyConfigurationCommands();
    }

    private void OnConfigLoaded(LocalServerProfileViewModel server)
    {
        _configBaselines[ConfigKey(server)] = server.ConfigText;
        LoadSimpleConfiguration(server);
        ValidateCurrentConfig(false);
        RefreshConfigDiff();
        LoadConfigHistory();
        NotifyConfigurationCommands();
    }

    private void OnConfigTextEdited()
    {
        ValidateCurrentConfig(false);
        RefreshConfigDiff();
    }

    private void LoadSimpleConfiguration(LocalServerProfileViewModel server)
    {
        var extension = Path.GetExtension(server.ConfigFile).ToLowerInvariant();
        if (extension == ".toml")
        {
            SimpleServerName = GetTomlValue(server.ConfigText, "game", "hostname") ?? server.Name;
            SimpleMaxPlayers = ParseInt(GetTomlValue(server.ConfigText, "game", "maxplayers"), 50);
            SimpleMap = GetTomlValue(server.ConfigText, "game", "map") ?? string.Empty;
            SimpleGamePreset = GetTomlValue(server.ConfigText, "game", "preset") ?? string.Empty;
            server.Port = ParseInt(GetTomlValue(server.ConfigText, "net", "port"), server.Port);
            return;
        }

        try
        {
            if (extension == ".json" && JsonNode.Parse(server.ConfigText) is JsonObject root)
            {
                var game = root["game"] as JsonObject;
                var net = root["net"] as JsonObject;
                SimpleServerName = game?["hostname"]?.GetValue<string>() ?? server.Name;
                SimpleMaxPlayers = game?["maxplayers"]?.GetValue<int>() ?? 50;
                SimpleMap = game?["map"]?.GetValue<string>() ?? string.Empty;
                SimpleGamePreset = game?["preset"]?.GetValue<string>() ?? string.Empty;
                server.Port = net?["port"]?.GetValue<int>() ?? server.Port;
                return;
            }
            if (extension is ".yml" or ".yaml")
            {
                var yaml = new YamlStream(); yaml.Load(new StringReader(server.ConfigText));
                if (yaml.Documents.FirstOrDefault()?.RootNode is YamlMappingNode yamlRoot)
                {
                    var game = GetYamlMapping(yamlRoot, "game"); var net = GetYamlMapping(yamlRoot, "net");
                    SimpleServerName = GetYamlScalar(game, "hostname") ?? server.Name;
                    SimpleMaxPlayers = ParseInt(GetYamlScalar(game, "maxplayers"), 50);
                    SimpleMap = GetYamlScalar(game, "map") ?? string.Empty;
                    SimpleGamePreset = GetYamlScalar(game, "preset") ?? string.Empty;
                    server.Port = ParseInt(GetYamlScalar(net, "port"), server.Port);
                }
            }
        }
        catch { }
    }

    private void ApplySimpleConfiguration()
    {
        var server = SelectedServer;
        if (server == null || string.IsNullOrWhiteSpace(server.ConfigFile) || server.IsRunning) return;
        try
        {
            var extension = Path.GetExtension(server.ConfigFile).ToLowerInvariant();
            if (extension == ".toml")
            {
                var text = server.ConfigText;
                text = SetTomlValue(text, "net", "port", server.Port.ToString());
                text = SetTomlValue(text, "game", "hostname", QuoteConfigString(SimpleServerName));
                text = SetTomlValue(text, "game", "maxplayers", SimpleMaxPlayers.ToString());
                text = SetTomlValue(text, "game", "map", QuoteConfigString(SimpleMap));
                text = SetTomlValue(text, "game", "preset", QuoteConfigString(SimpleGamePreset));
                server.ConfigText = text;
            }
            else if (extension == ".json")
            {
                var root = JsonNode.Parse(server.ConfigText) as JsonObject ?? [];
                var net = EnsureJsonObject(root, "net"); var game = EnsureJsonObject(root, "game");
                net["port"] = server.Port; game["hostname"] = SimpleServerName; game["maxplayers"] = SimpleMaxPlayers;
                game["map"] = SimpleMap; game["preset"] = SimpleGamePreset;
                server.ConfigText = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            }
            else if (extension is ".yml" or ".yaml")
            {
                var yaml = new YamlStream(); yaml.Load(new StringReader(server.ConfigText));
                var yamlRoot = yaml.Documents.FirstOrDefault()?.RootNode as YamlMappingNode ?? new YamlMappingNode();
                if (yaml.Documents.Count == 0) yaml.Documents.Add(new YamlDocument(yamlRoot));
                var net = EnsureYamlMapping(yamlRoot, "net"); var game = EnsureYamlMapping(yamlRoot, "game");
                SetYamlScalar(net, "port", server.Port.ToString()); SetYamlScalar(game, "hostname", SimpleServerName);
                SetYamlScalar(game, "maxplayers", SimpleMaxPlayers.ToString()); SetYamlScalar(game, "map", SimpleMap); SetYamlScalar(game, "preset", SimpleGamePreset);
                using var writer = new StringWriter(); yaml.Save(writer, false); server.ConfigText = writer.ToString();
            }
            else throw new InvalidDataException("Простой режим поддерживает TOML, YAML и JSON.");
            ValidateCurrentConfig(true);
            StatusText = "Поля применены к конфигурации. Нажмите «Сохранить», чтобы записать файл.";
        }
        catch (Exception e) { HasConfigErrors = true; ConfigValidationText = e.Message; }
    }

    private bool SaveSelectedConfigWithHistory()
    {
        var server = SelectedServer;
        if (server == null || string.IsNullOrWhiteSpace(server.ConfigFile) || server.IsRunning) return false;
        if (!ValidateCurrentConfig(true)) return false;
        try
        {
            var path = ResolveInsideServer(server, server.ConfigFile);
            if (File.Exists(path)) CreateConfigHistoryVersion(server, path, "Перед сохранением");
            File.WriteAllText(path, server.ConfigText, new UTF8Encoding(false));
            _configBaselines[ConfigKey(server)] = server.ConfigText;
            RefreshConfigDiff(); LoadConfigHistory(); SaveProfiles();
            StatusText = "Конфигурация проверена и сохранена. Предыдущая версия добавлена в историю.";
            return true;
        }
        catch (Exception e) { StatusText = "Не удалось сохранить конфигурацию: " + e.Message; return false; }
    }

    private bool ValidateCurrentConfig(bool showToast)
    {
        var server = SelectedServer;
        if (server == null || string.IsNullOrWhiteSpace(server.ConfigFile)) return false;
        try
        {
            var extension = Path.GetExtension(server.ConfigFile).ToLowerInvariant();
            switch (extension)
            {
                case ".json": JsonDocument.Parse(server.ConfigText); break;
                case ".yml": case ".yaml": var yaml = new YamlStream(); yaml.Load(new StringReader(server.ConfigText)); break;
                case ".toml": ValidateToml(server.ConfigText); break;
                default: if (string.IsNullOrWhiteSpace(server.ConfigText)) throw new InvalidDataException("Конфигурация пуста."); break;
            }
            HasConfigErrors = false; ConfigErrorLine = 0; ConfigValidationText = "Синтаксис корректен";
            if (showToast) _main.ShowToast("Синтаксис конфигурации корректен");
            return true;
        }
        catch (Exception e)
        {
            HasConfigErrors = true;
            ConfigErrorLine = ExtractErrorLine(e);
            ConfigValidationText = e.Message;
            if (showToast) _main.ShowToast("Ошибка конфигурации: " + e.Message, true);
            return false;
        }
    }

    private static void ValidateToml(string text)
    {
        var section = string.Empty; var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = NormalizeLines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = RemoveTomlComment(lines[i]).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('['))
            {
                if (!Regex.IsMatch(line, @"^\[[A-Za-z0-9_.-]+\]$")) throw new ConfigSyntaxException(i + 1, "Некорректный заголовок TOML-секции.");
                section = line[1..^1]; keys.Clear(); continue;
            }
            var match = Regex.Match(line, @"^([A-Za-z0-9_.-]+)\s*=\s*(.+)$");
            if (!match.Success) throw new ConfigSyntaxException(i + 1, "Ожидалась запись key = value.");
            if (!keys.Add(section + "." + match.Groups[1].Value)) throw new ConfigSyntaxException(i + 1, "Ключ повторяется в этой секции.");
            var value = match.Groups[2].Value.Trim();
            if (value.Length == 0 || HasUnbalancedQuotes(value) || HasUnbalancedBrackets(value))
                throw new ConfigSyntaxException(i + 1, "Значение TOML не завершено.");
        }
    }

    private void RefreshConfigDiff()
    {
        ConfigDiffLines.Clear();
        var server = SelectedServer;
        if (server == null || !_configBaselines.TryGetValue(ConfigKey(server), out var baseline))
        { ConfigDiffSummary = "Нет загруженной сохранённой версии."; NotifyDiff(); return; }
        var oldLines = NormalizeLines(baseline); var newLines = NormalizeLines(server.ConfigText);
        var max = Math.Max(oldLines.Length, newLines.Length);
        var changed = 0;
        for (var i = 0; i < max && ConfigDiffLines.Count < 240; i++)
        {
            var oldLine = i < oldLines.Length ? oldLines[i] : null; var newLine = i < newLines.Length ? newLines[i] : null;
            if (oldLine == newLine) continue;
            changed++;
            if (oldLine != null) ConfigDiffLines.Add(new LocalConfigDiffLineViewModel(i + 1, "−", oldLine, "#FF7777"));
            if (newLine != null) ConfigDiffLines.Add(new LocalConfigDiffLineViewModel(i + 1, "+", newLine, "#79C995"));
        }
        ConfigDiffSummary = changed == 0 ? "Изменений относительно сохранённой версии нет." : $"Изменено строк: {changed}";
        NotifyDiff();
    }

    private void NotifyDiff() => OnPropertyChanged(nameof(HasConfigDiff));

    private void CreateConfigHistoryVersion(LocalServerProfileViewModel server, string source, string description)
    {
        var directory = Path.Combine(_configHistoryRoot, server.Id); Directory.CreateDirectory(directory);
        var stem = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..6];
        var backup = Path.Combine(directory, stem + ".bak"); File.Copy(source, backup, true);
        var meta = new ConfigHistoryMetadata(DateTimeOffset.Now, description, server.ConfigFile, new FileInfo(backup).Length);
        File.WriteAllText(Path.Combine(directory, stem + ".json"), JsonSerializer.Serialize(meta, JsonOptions));
        foreach (var old in Directory.EnumerateFiles(directory, "*.bak").OrderByDescending(File.GetCreationTimeUtc).Skip(50))
        { try { File.Delete(old); File.Delete(Path.ChangeExtension(old, ".json")); } catch { } }
    }

    private void LoadConfigHistory()
    {
        ConfigHistory.Clear(); SelectedConfigHistoryEntry = null;
        var server = SelectedServer; if (server == null) return;
        var directory = Path.Combine(_configHistoryRoot, server.Id); if (!Directory.Exists(directory)) return;
        foreach (var backup in Directory.EnumerateFiles(directory, "*.bak").OrderByDescending(File.GetCreationTimeUtc))
        {
            try
            {
                var metaPath = Path.ChangeExtension(backup, ".json");
                var meta = File.Exists(metaPath) ? JsonSerializer.Deserialize<ConfigHistoryMetadata>(File.ReadAllText(metaPath)) : null;
                var info = new FileInfo(backup);
                ConfigHistory.Add(new LocalConfigHistoryEntryViewModel(backup, meta?.CreatedAt ?? info.CreationTime, meta?.Description ?? "Резервная копия", meta?.ConfigFile ?? server.ConfigFile, info.Length));
            }
            catch { }
        }
        NotifyConfigurationCommands();
    }

    private void RestoreSelectedConfigVersion()
    {
        if (SelectedServer == null || SelectedConfigHistoryEntry == null || SelectedServer.IsRunning) return;
        try
        {
            SelectedServer.ConfigText = File.ReadAllText(SelectedConfigHistoryEntry.Path);
            LoadSimpleConfiguration(SelectedServer); ValidateCurrentConfig(false); RefreshConfigDiff();
            StatusText = "Версия загружена в редактор. Проверьте изменения и нажмите «Сохранить».";
        }
        catch (Exception e) { StatusText = "Не удалось прочитать версию: " + e.Message; }
    }

    private string LastWorkingPath(LocalServerProfileViewModel server) => Path.Combine(_configHistoryRoot, server.Id, "last-working.config");
    private bool HasLastWorkingConfig() => SelectedServer is { } server && File.Exists(LastWorkingPath(server));

    private void MarkConfigAsWorking(LocalServerProfileViewModel server)
    {
        if (string.IsNullOrWhiteSpace(server.ConfigFile)) return;
        try
        {
            var source = ResolveInsideServer(server, server.ConfigFile); if (!File.Exists(source)) return;
            var target = LastWorkingPath(server); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, true);
            RestoreLastWorkingConfigCommand?.NotifyCanExecuteChanged();
        }
        catch { }
    }

    private void RestoreLastWorkingConfig()
    {
        var server = SelectedServer; if (server == null || server.IsRunning) return;
        try
        {
            server.ConfigText = File.ReadAllText(LastWorkingPath(server));
            LoadSimpleConfiguration(server); ValidateCurrentConfig(false); RefreshConfigDiff();
            StatusText = "Последнее рабочее состояние загружено в редактор. Для записи нажмите «Сохранить».";
        }
        catch (Exception e) { StatusText = "Не удалось восстановить рабочую конфигурацию: " + e.Message; }
    }

    private void NotifyConfigurationCommands()
    {
        ApplySimpleConfigCommand?.NotifyCanExecuteChanged(); ValidateConfigCommand?.NotifyCanExecuteChanged();
        RestoreConfigVersionCommand?.NotifyCanExecuteChanged(); RestoreLastWorkingConfigCommand?.NotifyCanExecuteChanged();
    }

    private static string? GetTomlValue(string text, string section, string key)
    {
        var current = string.Empty;
        foreach (var raw in NormalizeLines(text))
        {
            var line = RemoveTomlComment(raw).Trim();
            if (Regex.Match(line, @"^\[([^]]+)\]$") is { Success: true } sectionMatch) { current = sectionMatch.Groups[1].Value; continue; }
            if (!current.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var match = Regex.Match(line, "^" + Regex.Escape(key) + @"\s*=\s*(.+)$", RegexOptions.IgnoreCase);
            if (match.Success) return UnquoteConfigString(match.Groups[1].Value.Trim());
        }
        return null;
    }

    private static string SetTomlValue(string text, string section, string key, string value)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n"; var lines = NormalizeLines(text).ToList();
        var sectionStart = -1; var sectionEnd = lines.Count;
        for (var i = 0; i < lines.Count; i++)
        {
            var match = Regex.Match(lines[i].Trim(), @"^\[([^]]+)\]$");
            if (!match.Success) continue;
            if (sectionStart >= 0) { sectionEnd = i; break; }
            if (match.Groups[1].Value.Equals(section, StringComparison.OrdinalIgnoreCase)) sectionStart = i;
        }
        if (sectionStart < 0)
        {
            if (lines.Count > 0 && lines[^1].Length != 0) lines.Add(string.Empty);
            lines.Add($"[{section}]"); lines.Add($"{key} = {value}"); return string.Join(newline, lines);
        }
        for (var i = sectionStart + 1; i < sectionEnd; i++)
        {
            if (!Regex.IsMatch(RemoveTomlComment(lines[i]).Trim(), "^" + Regex.Escape(key) + @"\s*=", RegexOptions.IgnoreCase)) continue;
            var indent = Regex.Match(lines[i], @"^\s*").Value; lines[i] = $"{indent}{key} = {value}"; return string.Join(newline, lines);
        }
        lines.Insert(sectionEnd, $"{key} = {value}"); return string.Join(newline, lines);
    }

    private static string RemoveTomlComment(string line)
    {
        var quoted = false; var escaped = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i]; if (c == '\\' && quoted) { escaped = !escaped; continue; }
            if (c == '"' && !escaped) quoted = !quoted; escaped = false;
            if (c == '#' && !quoted) return line[..i];
        }
        return line;
    }
    private static bool HasUnbalancedQuotes(string value) { var quoted = false; var escaped = false; foreach (var c in value) { if (c == '\\' && quoted) { escaped = !escaped; continue; } if (c == '"' && !escaped) quoted = !quoted; escaped = false; } return quoted; }
    private static bool HasUnbalancedBrackets(string value) => value.Count(x => x == '[') != value.Count(x => x == ']') || value.Count(x => x == '{') != value.Count(x => x == '}');
    private static string QuoteConfigString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    private static string UnquoteConfigString(string value) => value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\") : value;
    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;
    private static string[] NormalizeLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    private static int ExtractErrorLine(Exception e) => e switch { ConfigSyntaxException syntax => syntax.Line, JsonException json when json.LineNumber.HasValue => (int)json.LineNumber.Value + 1, _ => Regex.Match(e.Message, @"line\s+(\d+)", RegexOptions.IgnoreCase) is { Success: true } match ? int.Parse(match.Groups[1].Value) : 0 };
    private static JsonObject EnsureJsonObject(JsonObject root, string key) { if (root[key] is JsonObject result) return result; result = []; root[key] = result; return result; }
    private static YamlMappingNode? GetYamlMapping(YamlMappingNode root, string key) => root.Children.TryGetValue(new YamlScalarNode(key), out var node) ? node as YamlMappingNode : null;
    private static YamlMappingNode EnsureYamlMapping(YamlMappingNode root, string key) { var result = GetYamlMapping(root, key); if (result != null) return result; result = new YamlMappingNode(); root.Children[new YamlScalarNode(key)] = result; return result; }
    private static string? GetYamlScalar(YamlMappingNode? root, string key) => root != null && root.Children.TryGetValue(new YamlScalarNode(key), out var node) ? (node as YamlScalarNode)?.Value : null;
    private static void SetYamlScalar(YamlMappingNode root, string key, string value) => root.Children[new YamlScalarNode(key)] = new YamlScalarNode(value);

    private sealed record ConfigHistoryMetadata(DateTimeOffset CreatedAt, string Description, string ConfigFile, long Size);
    private sealed class ConfigSyntaxException(int line, string message) : Exception($"Строка {line}: {message}") { public int Line { get; } = line; }
}

public sealed class LocalConfigDiffLineViewModel(int line, string marker, string text, string color)
{
    public int Line { get; } = line;
    public string LineText => Line.ToString();
    public string Marker { get; } = marker;
    public string Text { get; } = text;
    public string Color { get; } = color;
}

public sealed class LocalConfigHistoryEntryViewModel(string path, DateTimeOffset createdAt, string description, string configFile, long size)
{
    public string Path { get; } = path;
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public string CreatedText => CreatedAt.ToLocalTime().ToString("dd.MM.yyyy · HH:mm:ss");
    public string Description { get; } = description;
    public string ConfigFile { get; } = configFile;
    public string SizeText => size >= 1024 ? $"{size / 1024d:F1} КБ" : $"{size} Б";
}
