namespace Orbitra.Installer;

public sealed record InstallerStrings(
    string AppTitle, string Kicker, string Heading, string Description, string LatestRelease,
    string InstallPath, string Browse, string Install, string Installing, string ReadyTitle,
    string ReadyDescription, string LaunchAndLogin, string Close, string ErrorTitle, string Retry,
    string PathHint)
{
    public static InstallerStrings Russian { get; } = new(
        "ORBITRA INSTALLER", "ОРБИТАЛЬНАЯ СТАНЦИЯ ЗАПУСКА", "Установите Orbitra Launcher",
        "Установщик загрузит последний стабильный релиз с GitHub, проверит SHA-256 и безопасно разместит файлы в выбранной папке.",
        "Всегда устанавливается последняя версия", "Папка установки", "Обзор", "УСТАНОВИТЬ",
        "УСТАНОВКА", "ORBITRA ГОТОВА К ЗАПУСКУ",
        "При первом запуске откроется вход в аккаунт Space Station 14, затем - быстрая настройка лаунчера.",
        "ЗАПУСТИТЬ И ВОЙТИ", "ЗАКРЫТЬ", "Не удалось установить Orbitra", "ПОВТОРИТЬ",
        "Можно выбрать новую папку или обновить существующую установку Orbitra.");

    public static InstallerStrings English { get; } = new(
        "ORBITRA INSTALLER", "ORBITAL LAUNCH STATION", "Install Orbitra Launcher",
        "Setup downloads the latest stable GitHub release, verifies SHA-256, and safely installs it into the selected folder.",
        "The latest version is always installed", "Installation folder", "Browse", "INSTALL",
        "INSTALLING", "ORBITRA IS READY",
        "On first launch, sign in to your Space Station 14 account and complete the quick launcher setup.",
        "LAUNCH AND SIGN IN", "CLOSE", "Orbitra installation failed", "TRY AGAIN",
        "Choose a new folder or update an existing Orbitra installation.");
}
