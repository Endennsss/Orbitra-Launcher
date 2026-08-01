using System;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public class NewsEntryViewModel : ViewModelBase
{
    public NewsEntryViewModel(string headline, Uri? link = null, string? summary = null, string? date = null,
        string? id = null, string? version = null, bool important = false)
    {
        Headline = headline;
        Link = link;
        Summary = summary ?? string.Empty;
        Date = date ?? string.Empty;
        Id = id ?? headline;
        Version = version ?? string.Empty;
        Important = important;
    }

    public string Headline { get; }
    public Uri? Link { get; }
    public string Summary { get; }
    public string Date { get; }
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public bool HasDate => !string.IsNullOrWhiteSpace(Date);
    public bool HasLink => Link != null;
    public string Id { get; }
    public string Version { get; }
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    public bool Important { get; }

    public void Open()
    {
        if (Link != null)
            Helpers.OpenUri(Link);
    }
}
