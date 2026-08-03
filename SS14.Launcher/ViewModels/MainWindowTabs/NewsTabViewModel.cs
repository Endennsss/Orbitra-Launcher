using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CodeHollow.FeedReader;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using SS14.Launcher.Localization;
using SS14.Launcher.Utility;
using SS14.Launcher.Models.Data;
using Splat;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public partial class NewsTabViewModel : MainWindowTabViewModel
{
    private readonly DataManager _cfg = Locator.Current.GetRequiredService<DataManager>();
    private readonly MainWindowViewModel? _main;
    public ObservableList<NewsEntryViewModel> NewsEntries { get; } = [];
    public ObservableList<NewsEntryViewModel> LauncherNewsEntries { get; } =
    [
        new("Вышел Orbitra Launcher 0.43.0", summary: "Добавлены профили с аватарами, друзья, добровольный показ сервера и приглашения orbitra://. Мастерская получила обновление и предпросмотр тем, загрузчик — восстановление и ограничение скорости, а архивы теперь проходят усиленную проверку безопасности.", date: "3 августа 2026"),
        new("В Orbitra появились профили и друзья", summary: "Добавлена отдельная вкладка профиля с аватаром и статистикой игрового времени, заявки в друзья, добровольный показ текущего сервера, жалобы и приглашения orbitra:// для быстрого подключения.", date: "3 августа 2026"),
        new("Мастерская тем стала полноценнее", summary: "Добавлены обновления публикаций, управление своими темами и комментариями, избранное, поиск, сортировка, безопасный предпросмотр с возвратом и автоматические изображения карточек со скрытой тестовой страницы.", date: "3 августа 2026"),
        new("Мастерская переехала в отдельное окно", summary: "Каталог тем сообщества теперь открывается отдельным окном Orbitra, а в редактор кастомной темы добавлена плавная настройка затемнения обычного или GIF-фона.", date: "3 августа 2026"),
        new("Вышел Orbitra Launcher 0.42.0", summary: "Открыта мастерская тем сообщества с публикацией, установкой, лайками и комментариями от активного аккаунта SS14. Добавлен собственный C#-установщик, а ZIP-сборки теперь автоматически проверяются на целостность.", date: "2 августа 2026"),
        new("Orbitra получила собственный C#-установщик", summary: "Новый установщик в стиле Orbitra загружает последний стабильный GitHub Release в выбранную папку, проверяет SHA-256 и сразу предлагает запустить авторизацию и первичную настройку. В лаунчере оставлены русский и английский языки.", date: "2 августа 2026"),
        new("Вышел Orbitra Launcher 0.41.1", summary: "Улучшено качество нового логотипа во всех сборках, Discord RPC показывает изображение in_the_game во время игры, а описания серверов снова используют системные эмодзи.", date: "2 августа 2026"),
        new("Orbitra получила новый логотип", summary: "Фирменный знак обновлён в интерфейсе, окнах Windows, панели задач, установщике, сборках Linux и macOS, README и на сайте проекта.", date: "2 августа 2026"),
        new("Вышел Orbitra Launcher 0.41.0", summary: "Системный центр, безопасное автообновление, диагностика, управление серверным контентом, тихий пинг и новости сайта вошли в новый релиз.", date: "2 августа 2026"),
        new("Вышел Orbitra Launcher 0.40.1", summary: "Перед подключением Orbitra проверяет адрес, сеть, аккаунт, Loader и базу контента. Ошибки получили понятные причины, автовосстановление, быстрый переход к журналам и ручную проверку обновлений.", date: "2 августа 2026"),
        new("Фоны тем стали легче", summary: "Удалена поддержка MP4 и видеобиблиотеки. Для анимированных фонов оставлен стабильный зацикленный GIF, обычные изображения продолжают работать.", date: "1 августа 2026"),
        new("Полноценные кастомные темы", summary: "Добавлены палитра цветов, собственные фоны вкладок, размытие, предпросмотр перед импортом, библиотека тем и автоматическая проверка контраста.", date: "Последние изменения"),
        new("Центр активности и игровое время", summary: "Лаунчер сохраняет историю подключений и длительность игровых сессий по серверам, а важные события собираются в одном разделе.", date: "Последние изменения"),
        new("Улучшены избранные серверы и трей", summary: "Можно выбрать серверы для фоновой проверки, получать уведомления о доступности и новом раунде, а также менять аккаунт из меню трея.", date: "Последние изменения"),
        new("Discord RPC стал информативнее", summary: "Статус периодически обновляет сервер, ник, онлайн, пинг, карту и игровой режим с отдельными настройками приватности.", date: "Последние изменения"),
        new("Обновлён интерфейс лаунчера", summary: "Добавлены плавные переходы, Lucide-иконки, светлая тема, настраиваемая навигация, звуки интерфейса и полезные ссылки.", date: "Последние изменения")
    ];

    public NewsTabViewModel(MainWindowViewModel? main = null)
    {
        _main = main;
        RefreshUnreadCount();
    }
    public override string Name => LocalizationManager.Instance.GetString("tab-news-title");
    public override string IconData => "M4,3 L18,3 L18,21 L4,21 Z M8,7 L14,7 M8,11 L14,11 M8,15 L12,15 M18,7 L21,7 L21,19 Q21,21 19,21 L18,21";

    private bool _startedPullingNews;
    private bool _startedPullingLauncherNews;
    private int _unreadCount;

    public int UnreadCount
    {
        get => _unreadCount;
        private set { _unreadCount = value; BadgeChanged(); }
    }
    public override string BadgeText => UnreadCount > 0 ? UnreadCount.ToString() : string.Empty;

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
    public void ShowLauncherNews()
    {
        LauncherNewsSelected = true;
        MarkLauncherNewsRead();
    }

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
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Orbitra-Launcher");
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
                    entry.Date,
                    entry.Id,
                    entry.Version,
                    entry.Important))
                .ToArray();
            if (parsed.Length == 0) return;

            LauncherNewsEntries.Clear();
            LauncherNewsEntries.AddRange(parsed);
            RefreshUnreadCount();
            if (UnreadCount > 0 && parsed.FirstOrDefault(entry => entry.Important) is { } important)
                _main?.ShowToast($"Важная новость: {important.Headline}");
        }
        catch
        {
            // Built-in entries remain available when GitHub is unavailable.
        }
    }

    private void RefreshUnreadCount()
    {
        var lastRead = _cfg.GetCVar(CVars.LastReadLauncherNews);
        UnreadCount = string.IsNullOrWhiteSpace(lastRead)
            ? LauncherNewsEntries.Count
            : LauncherNewsEntries.TakeWhile(entry => !string.Equals(entry.Id, lastRead, StringComparison.Ordinal)).Count();
    }

    private void MarkLauncherNewsRead()
    {
        if (LauncherNewsEntries.FirstOrDefault() is not { } latest) return;
        _cfg.SetCVar(CVars.LastReadLauncherNews, latest.Id);
        _cfg.CommitConfig();
        UnreadCount = 0;
    }

    private sealed class LauncherNewsDto
    {
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Date { get; init; } = string.Empty;
        public string? Url { get; init; }
        public string Id { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public bool Important { get; init; }
    }
}
