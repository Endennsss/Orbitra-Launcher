using System;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace Orbitra.Installer.Bundle;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var runtimeDirectory = Path.Combine(Path.GetTempPath(), "OrbitraInstallerRuntime",
            Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(runtimeDirectory);
            using var payload = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Orbitra.Installer.Payload.zip")
                ?? throw new InvalidDataException("Orbitra installer payload is missing.");
            ExtractSafely(payload, runtimeDirectory);
            var installer = Path.Combine(runtimeDirectory, "Orbitra_Launcher_Installer.exe");
            if (!File.Exists(installer)) throw new FileNotFoundException("Orbitra installer executable is missing.");
            using var process = Process.Start(new ProcessStartInfo(installer)
            {
                WorkingDirectory = runtimeDirectory,
                UseShellExecute = false,
                Arguments = string.Join(' ', args.Select(QuoteArgument))
            }) ?? throw new InvalidOperationException("Unable to launch Orbitra Installer.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(runtimeDirectory)) Directory.Delete(runtimeDirectory, true); } catch { }
        }
    }

    private static void ExtractSafely(Stream payload, string destination)
    {
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var output = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer payload contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(output); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output, true);
        }
    }

    private static string QuoteArgument(string value) => '"' + value.Replace("\"", "\\\"") + '"';

    private static void ShowError(string message)
    {
        try
        {
            Process.Start(new ProcessStartInfo("msg.exe", $"* \"Orbitra Installer: {message.Replace("\"", "'")}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch { }
    }
}
