using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;
using Serilog;
using Splat;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Models.Logins;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

public static class OrbitraProtocol
{
    private static Timer? _presenceTimer;
    private static string? _activeServer;
    public static string CreateInvite(string serverAddress) =>
        $"orbitra://connect/?server={Uri.EscapeDataString(serverAddress)}";

    public static bool TryParseInvite(Uri uri, out Uri server)
    {
        server = null!;
        if (!uri.Scheme.Equals("orbitra", StringComparison.OrdinalIgnoreCase) ||
            !uri.Host.Equals("connect", StringComparison.OrdinalIgnoreCase)) return false;
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in query)
        {
            var pair = item.Split('=', 2);
            if (pair.Length != 2 || !pair[0].Equals("server", StringComparison.OrdinalIgnoreCase)) continue;
            var value = Uri.UnescapeDataString(pair[1]);
            if (UriHelper.TryParseSs14Uri(value, out var parsed)) { server = parsed; return true; }
        }
        return false;
    }

    public static void RegisterForCurrentUser()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var executable = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable)) return;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\orbitra");
            key.SetValue(null, "URL:Orbitra Launcher Protocol"); key.SetValue("URL Protocol", "");
            using var icon = key.CreateSubKey("DefaultIcon"); icon.SetValue(null, $"\"{executable}\",0");
            using var command = key.CreateSubKey(@"shell\open\command"); command.SetValue(null, $"\"{executable}\" \"%1\"");
        }
        catch (Exception e) { Log.Debug(e, "Unable to register orbitra protocol"); }
    }

    public static void PublishPresence(string? address)
    {
        _activeServer = address;
        _presenceTimer?.Dispose();
        _presenceTimer = string.IsNullOrWhiteSpace(address) ? null :
            new Timer(_ => SendPresence(), null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45));
        SendPresence();
    }

    private static async void SendPresence()
    {
        try
        {
            var cfg = Locator.Current.GetRequiredService<DataManager>();
            var account = Locator.Current.GetRequiredService<LoginManager>().ActiveAccount;
            if (account == null) return;
            var address = _activeServer;
            var share = cfg.GetCVar(CVars.OrbitraShareCurrentServer) && !string.IsNullOrWhiteSpace(address);
            using var social = new OrbitraSocialService();
            await social.UpdatePresenceAsync(account.UserId, share, share ? address : null, share ? address : null);
        }
        catch (Exception e) { Log.Debug(e, "Unable to update Orbitra presence"); }
    }
}
