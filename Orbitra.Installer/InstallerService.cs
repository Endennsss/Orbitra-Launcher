using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Orbitra.Installer;

internal sealed class InstallerService : IDisposable
{
    private const string LatestReleaseApi = "https://api.github.com/repos/Endennsss/Orbitra-Launcher/releases/latest";
    private const string PortableAssetName = "Orbitra_Launcher_Windows.zip";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };

    public InstallerService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("Orbitra-Installer/1.0");

    public async Task<InstallResult> InstallLatestAsync(string destination, bool desktopShortcut,
        bool startMenuShortcut, IProgress<InstallProgress> progress, CancellationToken cancellationToken)
    {
        destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(destination.Trim()));
        ValidateDestination(destination);

        progress.Report(new(0.02, "Получение последнего релиза…", "Checking the latest release…"));
        var releaseJson = await _http.GetStringAsync(LatestReleaseApi, cancellationToken);
        var release = JsonSerializer.Deserialize(releaseJson, InstallerJsonContext.Default.ReleaseDto)
            ?? throw new InvalidDataException("GitHub вернул пустое описание релиза.");
        if (release.Draft || release.Prerelease)
            throw new InvalidDataException("Последний релиз не является стабильным.");

        var asset = release.Assets.FirstOrDefault(item =>
            item.Name.Equals(PortableAssetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"В релизе {release.TagName} отсутствует {PortableAssetName}.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "OrbitraInstaller", Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(tempRoot, PortableAssetName);
        var extractPath = Path.Combine(tempRoot, "payload");
        Directory.CreateDirectory(tempRoot);

        try
        {
            progress.Report(new(0.06, $"Скачивание Orbitra {release.TagName}…", $"Downloading Orbitra {release.TagName}…"));
            using (var response = await _http.GetAsync(asset.BrowserDownloadUrl,
                       HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? asset.Size;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1024 * 128, true);
                var buffer = new byte[1024 * 128];
                long downloaded = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    var fraction = total > 0 ? (double)downloaded / total : 0;
                    progress.Report(new(0.06 + fraction * 0.64,
                        $"Скачивание… {FormatBytes(downloaded)} / {FormatBytes(total)}",
                        $"Downloading… {FormatBytes(downloaded)} / {FormatBytes(total)}"));
                }
            }

            progress.Report(new(0.73, "Проверка SHA-256…", "Verifying SHA-256…"));
            var actualHash = await CalculateSha256Async(archivePath, cancellationToken);
            var expectedHash = NormalizeDigest(asset.Digest);
            if (expectedHash == null)
                throw new InvalidDataException("GitHub Release не содержит SHA-256 для установочного архива.");
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 не совпал. Установка остановлена для защиты файлов.");

            progress.Report(new(0.78, "Распаковка файлов…", "Extracting files…"));
            Directory.CreateDirectory(extractPath);
            ExtractSafely(archivePath, extractPath, cancellationToken);
            var payloadRoot = FindPayloadRoot(extractPath);

            progress.Report(new(0.88, "Установка Orbitra Launcher…", "Installing Orbitra Launcher…"));
            ReplaceInstallation(payloadRoot, destination);
            File.WriteAllText(Path.Combine(destination, ".orbitra-install"), release.TagName);
            var executable = Path.Combine(destination, "Orbitra Launcher.exe");
            if (desktopShortcut)
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "Orbitra Launcher.lnk"), executable);
            if (startMenuShortcut)
            {
                var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Orbitra Launcher");
                Directory.CreateDirectory(startMenu);
                CreateShortcut(Path.Combine(startMenu, "Orbitra Launcher.lnk"), executable);
            }
            progress.Report(new(1, "Установка завершена", "Installation completed"));
            return new InstallResult(destination, release.TagName, executable);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static void ValidateDestination(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination)) throw new InvalidDataException("Выберите папку установки.");
        var root = Path.GetPathRoot(destination);
        if (string.IsNullOrWhiteSpace(root) || destination.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Нельзя устанавливать лаунчер в корень диска.");
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any() &&
            !File.Exists(Path.Combine(destination, "Orbitra Launcher.exe")) &&
            !File.Exists(Path.Combine(destination, ".orbitra-install")))
            throw new InvalidDataException("Выбранная папка не пуста и не является установкой Orbitra Launcher.");
    }

    private static async Task<string> CalculateSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var value = digest.Trim();
        return value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? value[7..] : value;
    }

    private static void ExtractSafely(string archivePath, string destination, CancellationToken cancellationToken)
    {
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!output.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Архив содержит небезопасный путь.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(output); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output, true);
        }
    }

    private static string FindPayloadRoot(string extracted)
    {
        if (File.Exists(Path.Combine(extracted, "Orbitra Launcher.exe"))) return extracted;
        var directories = Directory.GetDirectories(extracted);
        if (directories.Length == 1 && File.Exists(Path.Combine(directories[0], "Orbitra Launcher.exe")))
            return directories[0];
        throw new InvalidDataException("В архиве не найден Orbitra Launcher.exe.");
    }

    private static void ReplaceInstallation(string payload, string destination)
    {
        var parent = Directory.GetParent(destination)?.FullName
                     ?? throw new InvalidDataException("Некорректная папка установки.");
        Directory.CreateDirectory(parent);
        var staged = Path.Combine(parent, $".orbitra-stage-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".orbitra-backup-{Guid.NewGuid():N}");
        CopyDirectory(payload, staged);
        var hadExisting = Directory.Exists(destination);
        try
        {
            if (hadExisting) Directory.Move(destination, backup);
            Directory.Move(staged, destination);
        }
        catch
        {
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            if (!Directory.Exists(destination) && Directory.Exists(backup)) Directory.Move(backup, destination);
            throw;
        }
        if (hadExisting)
        {
            try { Directory.Delete(backup, true); } catch { }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
    }

    private static void CreateShortcut(string shortcutPath, string executable)
    {
        // InternetShortcut is understood natively by Windows Explorer and avoids runtime COM,
        // which keeps the installer compatible with Native AOT.
        var urlPath = Path.ChangeExtension(shortcutPath, ".url");
        var fullExecutable = Path.GetFullPath(executable);
        var fileUri = new UriBuilder(Uri.UriSchemeFile, string.Empty)
        {
            Path = fullExecutable
        }.Uri.AbsoluteUri;
        File.WriteAllText(urlPath,
            $"[InternetShortcut]{Environment.NewLine}URL={fileUri}{Environment.NewLine}" +
            $"IconFile={fullExecutable}{Environment.NewLine}IconIndex=0{Environment.NewLine}");
    }

    public static void Launch(string executable) => Process.Start(new ProcessStartInfo(executable)
    {
        WorkingDirectory = Path.GetDirectoryName(executable)!,
        UseShellExecute = true
    });

    private static string FormatBytes(long value) => value >= 1024 * 1024
        ? $"{value / 1024d / 1024d:0.0} MB"
        : $"{value / 1024d:0} KB";

    public void Dispose() => _http.Dispose();

}

internal sealed record InstallProgress(double Value, string Russian, string English);
internal sealed record InstallResult(string Directory, string Version, string Executable);

internal sealed record ReleaseDto(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("assets")] AssetDto[] Assets);

internal sealed record AssetDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("digest")] string? Digest);

[JsonSerializable(typeof(ReleaseDto))]
internal sealed partial class InstallerJsonContext : JsonSerializerContext;
