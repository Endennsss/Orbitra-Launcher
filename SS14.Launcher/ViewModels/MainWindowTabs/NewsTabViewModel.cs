using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CodeHollow.FeedReader;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using SS14.Launcher.Localization;
using SS14.Launcher.Utility;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public partial class NewsTabViewModel : MainWindowTabViewModel
{
    public ObservableList<NewsEntryViewModel> NewsEntries { get; } = [];
    public ObservableList<NewsEntryViewModel> LauncherNewsEntries { get; } =
    [
        new("Фоны тем стали легче", summary: "Удалена поддержка MP4 и видеобиблиотеки. Для анимированных фонов оставлен стабильный зацикленный GIF, обычные изображения продолжают работать.", date: "1 августа 2026"),
        new("Полноценные кастомные темы", summary: "Добавлены палитра цветов, собственные фоны вкладок, размытие, предпросмотр перед импортом, библиотека тем и автоматическая проверка контраста.", date: "Последние изменения"),
        new("Центр активности и игровое время", summary: "Лаунчер сохраняет историю подключений и длительность игровых сессий по серверам, а важные события собираются в одном разделе.", date: "Последние изменения"),
        new("Улучшены избранные серверы и трей", summary: "Можно выбрать серверы для фоновой проверки, получать уведомления о доступности и новом раунде, а также менять аккаунт из меню трея.", date: "Последние изменения"),
        new("Discord RPC стал информативнее", summary: "Статус периодически обновляет сервер, ник, онлайн, пинг, карту и игровой режим с отдельными настройками приватности.", date: "Последние изменения"),
        new("Обновлён интерфейс лаунчера", summary: "Добавлены плавные переходы, Lucide-иконки, светлая тема, настраиваемая навигация, звуки интерфейса и полезные ссылки.", date: "Последние изменения")
    ];
    public override string Name => LocalizationManager.Instance.GetString("tab-news-title");
    public override string IconData => "M4,3 L18,3 L18,21 L4,21 Z M8,7 L14,7 M8,11 L14,11 M8,15 L12,15 M18,7 L21,7 L21,19 Q21,21 19,21 L18,21";

    private bool _startedPullingNews;
    private bool _startedPullingLauncherNews;

    private bool _launcherNewsSelected;

    public bool LauncherNewsSelected
    {
        get => _launcherNewsSelected;
        set
        {
            if (_launcherNewsSelected == value) return;
            _launcherNewsSelected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OriginalNewsSelected));
        }
    }

    public bool OriginalNewsSelected => !LauncherNewsSelected;

    [ObservableProperty]
    private bool _newsPulled;

    [ObservableProperty]
    private bool _newsLoadFailed;

    public void ShowOriginalNews() => LauncherNewsSelected = false;
    public void ShowLauncherNews() => LauncherNewsSelected = true;

    public override void Selected()
    {
        base.Selected();

        PullNews();
        PullLauncherNews();
    }

    private async void PullNews()
    {
        if (_startedPullingNews)
            return;

        _startedPullingNews = true;
        try
        {
            var feed = await FeedReader.ReadAsync(ConfigConstants.NewsFeedUrl);
            NewsEntries.AddRange(feed.Items.Select(i => new NewsEntryViewModel(i.Title, new Uri(i.Link))));
            NewsPulled = true;
        }
        catch
        {
            NewsLoadFailed = true;
            NewsPulled = true;
        }
    }

    private async void PullLauncherNews()
    {
        if (_startedPullingLauncherNews) return;
        _startedPullingLauncherNews = true;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SS14-Custom-Launcher");
            var json = await client.GetStringAsync(ConfigConstants.LauncherNewsUrl);
            var entries = JsonSerializer.Deserialize<LauncherNewsDto[]>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (entries is not { Length: > 0 }) return;

            var parsed = entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Title))
                .Select(entry => new NewsEntryViewModel(
                    entry.Title,
                    Uri.TryCreate(entry.Url, UriKind.Absolute, out var link) ? link : null,
                    entry.Summary,
                    entry.Date))
                .ToArray();
            if (parsed.Length == 0) return;

            LauncherNewsEntries.Clear();
            LauncherNewsEntries.AddRange(parsed);
        }
        catch
        {
            // Built-in entries remain available when GitHub is unavailable.
        }
    }

    private sealed class LauncherNewsDto
    {
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Date { get; init; } = string.Empty;
        public string? Url { get; init; }
    }
}
