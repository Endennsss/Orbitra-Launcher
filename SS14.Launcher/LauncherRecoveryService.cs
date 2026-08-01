using System;
using System.IO;
using Serilog;

namespace SS14.Launcher;

public sealed class LauncherRecoveryService
{
    private readonly string _marker = Path.Combine(LauncherPaths.DirUserData, "launcher-running.lock");
    public bool PreviousRunFailed { get; private set; }
    public string CrashReportPath => Path.Combine(LauncherPaths.DirLogs, "last-crash.txt");

    public void Begin()
    {
        PreviousRunFailed = File.Exists(_marker);
        File.WriteAllText(_marker, $"Started {DateTimeOffset.Now:O}");
    }

    public void RecordCrash(Exception exception)
    {
        try { File.WriteAllText(CrashReportPath, $"{DateTimeOffset.Now:O}\n{exception}"); }
        catch (Exception e) { Log.Warning(e, "Unable to save crash report"); }
    }

    public void CompleteCleanExit()
    {
        try { if (File.Exists(_marker)) File.Delete(_marker); }
        catch (Exception e) { Log.Warning(e, "Unable to clear launcher run marker"); }
    }
}
