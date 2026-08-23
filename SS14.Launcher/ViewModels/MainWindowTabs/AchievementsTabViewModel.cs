using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class AchievementsTabViewModel : MainWindowTabViewModel
{
    public ObservableCollection<OrbitraAchievementViewModel> Achievements { get; } = [];

    public override string Name => "Достижения";
    // Official Lucide "trophy" icon.
    public override string IconData => "M8,21 L16,21 M12,17 L12,21 M7,4 L17,4 L17,9 A5,5 0 0 1 7,9 Z M7,6 L5,6 A3,3 0 0 0 8,12 M17,6 L19,6 A3,3 0 0 1 16,12";

    public string TotalPlaytime { get; private set; } = "меньше минуты";
    public string ServersVisited { get; private set; } = "0";
    public string FavoriteServer { get; private set; } = "Пока не определён";
    public string UnlockedText { get; private set; } = "0 / 0";
    public double CompletionProgress { get; private set; }

    public AchievementsTabViewModel()
    {
        PlaytimeTracker.Changed += _ => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    public override void Selected() => Refresh();

    private void Refresh()
    {
        var entries = PlaytimeTracker.GetAll().Where(x => x.Duration > TimeSpan.Zero).ToArray();
        var total = entries.Aggregate(TimeSpan.Zero, (sum, item) => sum + item.Duration);
        var longest = entries.OrderByDescending(x => x.Duration).FirstOrDefault();
        var totalHours = total.TotalHours;
        var serverCount = entries.Length;
        var longestHours = longest?.Duration.TotalHours ?? 0;

        TotalPlaytime = PlaytimeTabViewModel.Format(total);
        ServersVisited = serverCount.ToString("N0");
        FavoriteServer = longest?.Name ?? "Пока не определён";

        Achievements.Clear();
        Add("Первый сигнал", "Провести в игре хотя бы 15 минут.", "M5,12 L10,17 L19,8", total.TotalMinutes, 15, "мин");
        Add("Полная смена", "Набрать один час общего игрового времени.", "M12,2 A10,10 0 1 1 2,12 A10,10 0 0 1 12,2 Z M12,6 L12,12 L16,14", totalHours, 1, "ч");
        Add("Опытный экипаж", "Провести на станциях 10 часов.", "M12,2 L15.09,8.26 L22,9.27 L17,14.14 L18.18,21.02 L12,17.77 L5.82,21.02 L7,14.14 L2,9.27 L8.91,8.26 Z", totalHours, 10, "ч");
        Add("Ветеран орбиты", "Набрать 50 часов общего времени.", "M12,15 L8.5,17 L9.5,13 L6.5,10.5 L10.5,10 L12,6.5 L13.5,10 L17.5,10.5 L14.5,13 L15.5,17 Z M12,2 A10,10 0 1 1 2,12", totalHours, 50, "ч");
        Add("Центурион", "Преодолеть отметку в 100 часов.", "M8,21 L16,21 M12,17 L12,21 M7,4 L17,4 L17,9 A5,5 0 0 1 7,9 Z", totalHours, 100, "ч");
        Add("Дальний космос", "Набрать 250 часов в Space Station 14.", "M12,3 L14.5,8.5 L20,9 L16,13 L17,19 L12,16 L7,19 L8,13 L4,9 L9.5,8.5 Z", totalHours, 250, "ч");

        Add("Исследователь", "Посетить 3 разных сервера.", "M3,11 L22,2 L13,21 L11,13 Z", serverCount, 3, "серверов");
        Add("Навигатор", "Посетить 10 разных серверов.", "M12,2 A10,10 0 1 1 2,12 A10,10 0 0 1 12,2 Z M16.24,7.76 L14.12,14.12 L7.76,16.24 L9.88,9.88 Z", serverCount, 10, "серверов");
        Add("Коллекционер станций", "Посетить 25 разных серверов.", "M4,19.5 A2.5,2.5 0 0 1 6.5,17 L20,17 M6.5,2 L20,2 L20,22 L6.5,22 A2.5,2.5 0 0 1 4,19.5 L4,4.5 A2.5,2.5 0 0 1 6.5,2", serverCount, 25, "серверов");
        Add("Своя станция", "Провести 10 часов на одном сервере.", "M3,21 L21,21 M5,21 L5,7 L12,3 L19,7 L19,21 M9,21 L9,13 L15,13 L15,21", longestHours, 10, "ч");
        Add("Дом среди звёзд", "Провести 50 часов на одном сервере.", "M3,11 L12,3 L21,11 L21,21 L3,21 Z M9,21 L9,14 L15,14 L15,21", longestHours, 50, "ч");

        var unlocked = Achievements.Count(x => x.IsUnlocked);
        UnlockedText = $"{unlocked} / {Achievements.Count}";
        CompletionProgress = Achievements.Count == 0 ? 0 : (double)unlocked / Achievements.Count;
        OnPropertyChanged(nameof(TotalPlaytime));
        OnPropertyChanged(nameof(ServersVisited));
        OnPropertyChanged(nameof(FavoriteServer));
        OnPropertyChanged(nameof(UnlockedText));
        OnPropertyChanged(nameof(CompletionProgress));
        BadgeChanged();
    }

    private void Add(string title, string description, string iconData, double current, double target, string unit)
        => Achievements.Add(new OrbitraAchievementViewModel(title, description, iconData, current, target, unit));

    public override string BadgeText => Achievements.Count(x => x.IsUnlocked).ToString();
}

public sealed class OrbitraAchievementViewModel
{
    public string Title { get; }
    public string Description { get; }
    public string IconData { get; }
    public double Progress { get; }
    public bool IsUnlocked { get; }
    public string ProgressText { get; }
    public string StateText => IsUnlocked ? "ПОЛУЧЕНО" : "В ПРОЦЕССЕ";

    public OrbitraAchievementViewModel(string title, string description, string iconData,
        double current, double target, string unit)
    {
        Title = title;
        Description = description;
        IconData = iconData;
        Progress = target <= 0 ? 1 : Math.Clamp(current / target, 0, 1);
        IsUnlocked = current >= target;
        ProgressText = unit == "серверов"
            ? $"{Math.Min((int)current, (int)target)} / {(int)target} {unit}"
            : $"{Math.Min(current, target):0.#} / {target:0.#} {unit}";
    }
}
