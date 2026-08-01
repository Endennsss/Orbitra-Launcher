using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace SS14.Launcher;

public static class LauncherUpdateService
{
    public static async Task StageAndRestartAsync(string version, string downloadUrl, string expectedDigest,
        Action<double>? progress = null)
    {
#if !FULL_RELEASE
        throw new InvalidOperationException("Автоустановка доступна только в установленной release-сборке.");
#else
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Автоустановка пока поддерживается только на Windows.");
        if (!expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub не предоставил SHA-256 для этого релиза.");

        var updateRoot = Path.Combine(LauncherPaths.DirLocalData, "updates", Sanitize(version));
        var archivePath = Path.Combine(updateRoot, "update.zip");
        var stagingPath = Path.Combine(updateRoot, "staging");
        Directory.CreateDirectory(updateRoot);
        if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);
        Directory.CreateDirectory(stagingPath);

        using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Orbitra-Launcher");
            using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = File.Create(archivePath);
            var buffer = new byte[128 * 1024];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read));
                written += read;
                if (total > 0) progress?.Invoke(written / (double)total);
            }
        }

        await using (var stream = File.OpenRead(archivePath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            var expected = expectedDigest["sha256:".Length..].Trim();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 загруженного обновления не совпадает с GitHub Releases.");
        }

        using (var archive = ZipFile.OpenRead(archivePath))
        {
            var root = Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar;
            foreach (var entry in archive.Entries)
            {
                var destination = Path.GetFullPath(Path.Combine(stagingPath, entry.FullName));
                if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Обновление содержит небезопасный путь.");
                if (string.IsNullOrEmpty(entry.Name)) Directory.CreateDirectory(destination);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, true);
                }
            }
        }

        var installRoot = Path.GetFullPath(Path.Combine(LauncherPaths.DirLauncherInstall, ".."));
        var scriptPath = Path.Combine(updateRoot, "install-update.ps1");
        var script = """
            param([int]$ProcessId, [string]$Source, [string]$Destination, [string]$Executable)
            Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
            Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
            Start-Process -FilePath $Executable
            """;
        await File.WriteAllTextAsync(scriptPath, script);
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                "-ProcessId", Environment.ProcessId.ToString(), "-Source", stagingPath,
                "-Destination", installRoot, "-Executable", Path.Combine(installRoot, "Orbitra Launcher.exe") },
            UseShellExecute = false,
            CreateNoWindow = true
        });
#endif
    }

    private static string Sanitize(string version)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) version = version.Replace(invalid, '_');
        return version;
    }
}
