using System;
using System.Threading;
using Avalonia;

namespace Orbitra.Installer;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--silent-install", var directory])
        {
            try
            {
                using var installer = new InstallerService();
                installer.InstallLatestAsync(directory, false, false,
                    new Progress<InstallProgress>(), CancellationToken.None).GetAwaiter().GetResult();
                return 0;
            }
            catch { return 1; }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();
}
