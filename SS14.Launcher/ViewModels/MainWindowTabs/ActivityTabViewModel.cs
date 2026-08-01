using System;
using System.Collections.ObjectModel;
using System.Linq;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class ActivityTabViewModel : MainWindowTabViewModel
{
    public ObservableCollection<ActivityEntryViewModel> Entries { get; } = [];
    public bool IsEmpty => Entries.Count == 0;
    public override string Name => "Активность";
    // Official Lucide "activity".
    public override string IconData => "M22,12 L19.52,12 A2,2 0 0 0 17.59,13.46 L15.24,21.82 A0.25,0.25 0 0 1 14.76,21.82 L9.24,2.18 A0.25,0.25 0 0 0 8.76,2.18 L6.41,10.54 A2,2 0 0 1 4.49,12 L2,12";
    public ActivityTabViewModel() => Refresh();
    public override void Selected() => Refresh();
    public void Clear()
    {
        ActivityLog.Clear();
        Refresh();
    }

    private void Refresh()
    {
        var items = ActivityLog.GetEntries().Select(x => new ActivityEntryViewModel(x.Time, x.Category, x.Title, x.Details, x.IsError)).ToList();
        Entries.Clear();
        foreach (var item in items.OrderByDescending(x => x.Time).Take(300)) Entries.Add(item);
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed class ActivityEntryViewModel
{
    public DateTimeOffset Time { get; }
    public string TimeText => Time.LocalDateTime.ToString("dd.MM · HH:mm:ss");
    public string Category { get; }
    public string Title { get; }
    public string Details { get; }
    public bool IsError { get; }
    public ActivityEntryViewModel(DateTimeOffset time, string category, string title, string details, bool error)
    { Time = time; Category = category; Title = title; Details = details; IsError = error; }
}
