using System;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public class NewsEntryViewModel : ViewModelBase
{
    public NewsEntryViewModel(string headline, Uri? link = null, string? summary = null, string? date = null)
    {
        Headline = headline;
        Link = link;
        Summary = summary ?? string.Empty;
        Date = date ?? string.Empty;
    }

    public string Headline { get; }
    public Uri? Link { get; }
    public string Summary { get; }
    public string Date { get; }
    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);
    public bool HasDate => !string.IsNullOrWhiteSpace(Date);
    public bool HasLink => Link != null;

    public void Open()
    {
        if (Link != null)
            Helpers.OpenUri(Link);
    }
}
