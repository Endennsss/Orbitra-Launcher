#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace SS14.Launcher.Tests;

[TestFixture]
public sealed class WindowsJobObjectTests
{
    [Test]
    public async Task ClosingJobTerminatesCompleteProcessTree()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Windows Job Objects are only available on Windows.");

        var gate = Path.Combine(Path.GetTempPath(), $"orbitra-job-test-{Guid.NewGuid():N}.ready");
        var pidFile = Path.Combine(Path.GetTempPath(), $"orbitra-job-test-{Guid.NewGuid():N}.pid");
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var escapedGate = gate.Replace("'", "''");
        var escapedPidFile = pidFile.Replace("'", "''");
        var escapedPowerShell = powershell.Replace("'", "''");
        var script = $"while (!(Test-Path -LiteralPath '{escapedGate}')) {{ Start-Sleep -Milliseconds 20 }}; " +
                     $"$child = Start-Process -FilePath '{escapedPowerShell}' -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 60' -PassThru; " +
                     $"$child.Id | Set-Content -LiteralPath '{escapedPidFile}'; Wait-Process -Id $child.Id";
        using var parent = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = powershell,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        parent.StartInfo.ArgumentList.Add("-NoProfile");
        parent.StartInfo.ArgumentList.Add("-NonInteractive");
        parent.StartInfo.ArgumentList.Add("-Command");
        parent.StartInfo.ArgumentList.Add(script);

        WindowsJobObject? job = null;
        int childPid = 0;
        try
        {
            Assert.That(parent.Start(), Is.True);
            job = WindowsJobObject.CreateAndAssign(parent);
            File.WriteAllText(gate, "ready");
            for (var attempt = 0; attempt < 100 && !File.Exists(pidFile); attempt++)
                await Task.Delay(50);
            var pidLine = File.Exists(pidFile) ? File.ReadAllText(pidFile).Trim() : null;
            var diagnostic = parent.HasExited ? await parent.StandardError.ReadToEndAsync() : "The parent PowerShell process is still running.";
            Assert.That(int.TryParse(pidLine, out childPid), Is.True, "The child process did not report its PID. " + diagnostic);
            Assert.That(ProcessExists(childPid), Is.True);

            job.Dispose();
            job = null;

            for (var attempt = 0; attempt < 50 && ProcessExists(childPid); attempt++)
                await Task.Delay(100);
            Assert.That(ProcessExists(parent.Id), Is.False, "The assigned parent process was not terminated.");
            Assert.That(ProcessExists(childPid), Is.False, "A child process escaped the Job Object.");
        }
        finally
        {
            job?.Dispose();
            try { if (!parent.HasExited) parent.Kill(true); } catch { }
            if (childPid != 0)
            {
                try { Process.GetProcessById(childPid).Kill(true); } catch { }
            }
            try { File.Delete(gate); } catch { }
            try { File.Delete(pidFile); } catch { }
        }
    }

    private static bool ProcessExists(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
