using System;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class UsefulLinksTabViewModel : MainWindowTabViewModel
{
    public override string Name => "Полезные ссылки";

    // Official Lucide "link" icon.
    public override string IconData =>
        "M10,13 A5,5 0 0 0 17.54,13.54 L20.54,10.54 A5,5 0 0 0 13.46,3.46 L11.74,5.17 M14,11 A5,5 0 0 0 6.46,10.46 L3.46,13.46 A5,5 0 0 0 10.54,20.54 L12.25,18.83";

    public void OpenChemHelper() => Helpers.OpenUri(new Uri(ConfigConstants.ChemHelperUrl));
    public void OpenSs14Website() => Helpers.OpenUri(new Uri(ConfigConstants.WebsiteUrl));
    public void OpenAccount() => Helpers.OpenUri(new Uri(ConfigConstants.AccountManagementUrl));
    public void OpenDocumentation() => Helpers.OpenUri(new Uri("https://docs.spacestation14.com/"));
    public void OpenGitHub() => Helpers.OpenUri(new Uri("https://github.com/space-wizards/space-station-14"));
    public void OpenLauncherSite() => Helpers.OpenUri(new Uri(ConfigConstants.CustomLauncherSiteUrl));
    public void OpenLauncherGitHub() => Helpers.OpenUri(new Uri(ConfigConstants.CustomLauncherRepositoryUrl));
    public void OpenLauncherReleases() => Helpers.OpenUri(new Uri(ConfigConstants.CustomLauncherReleasesUrl));
    public void DownloadLatestLauncher() => Helpers.OpenUri(new Uri(ConfigConstants.CustomLauncherLatestDownloadUrl));
    public void OpenLauncherIssues() => Helpers.OpenUri(new Uri(ConfigConstants.CustomLauncherIssuesUrl));
    public void OpenLauncherActions() => Helpers.OpenUri(new Uri(ConfigConstants.CustomLauncherActionsUrl));
}
