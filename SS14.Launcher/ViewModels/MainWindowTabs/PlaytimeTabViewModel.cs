using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class PlaytimeTabViewModel : MainWindowTabViewModel
{
    public ObservableCollection<PlaytimeEntryViewModel> Servers { get; } = [];
    public string TotalText { get; private set; } = "0 мин";
    public bool IsEmpty => Servers.Count == 0;
    public override string Name => "Игровое время";
    // Official Lucide "chart-no-axes-column-increasing".
    public override string IconData => "M5,21 L5,15 M12,21 L12,9 M19,21 L19,3";

    public PlaytimeTabViewModel() => Refresh();
    public override void Selected() => Refresh();

    private void Refresh()
    {
        Servers.Clear();
        foreach (var entry in PlaytimeTracker.GetAll()) Servers.Add(new PlaytimeEntryViewModel(entry));
        TotalText = Format(Servers.Aggregate(TimeSpan.Zero, (total, item) => total + item.Duration));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(IsEmpty));
    }

    internal static string Format(TimeSpan time) => time.TotalHours >= 1
        ? $"{(int)time.TotalHours} ч {time.Minutes} мин"
        : time.TotalMinutes >= 1 ? $"{(int)time.TotalMinutes} мин" : "меньше минуты";
}

public sealed class PlaytimeEntryViewModel
{
    public string Name { get; }
    public string Address { get; }
    public TimeSpan Duration { get; }
    public string DurationText => PlaytimeTabViewModel.Format(Duration);
    public string LastPlayedText { get; }
    public PlaytimeEntryViewModel(PlaytimeServerEntry entry)
    {
        Name = entry.Name; Address = entry.Address; Duration = entry.Duration;
        LastPlayedText = entry.LastPlayed is { } date && date > DateTimeOffset.MinValue
            ? date.LocalDateTime.ToString("dd.MM.yyyy · HH:mm") : "Нет данных";
    }
}
