using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Views;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class ProfileTabViewModel : MainWindowTabViewModel, IDisposable
{
    public static readonly Guid CreatorId = Guid.Parse("1d17bc4a-25fa-4ef9-af88-fb1d4dd71701");
    private readonly MainWindowViewModel _main;
    private readonly OrbitraSocialService _social = new();
    private readonly ThemeWorkshopService _workshop = new();
    private bool _busy;
    private string _status = "";
    private string _search = "";
    private OrbitraProfileDto? _found;
    private bool _showFoundProfileInSearch;
    private Bitmap? _avatar;
    private Bitmap? _banner;
    private Bitmap? _foundAvatar;
    private Bitmap? _foundBanner;
    private string _description = "";
    private FavoriteServerOption? _favoriteServer;
    private readonly Timer _socialPollTimer;
    private readonly HashSet<Guid> _knownIncoming = [];
    private bool _pollInitialized;
    private ProfileSettingsWindow? _settingsWindow;
    private OrbitraProfileWindow? _profileWindow;
    public ObservableCollection<OrbitraFriendItemViewModel> Friends { get; } = [];
    public ObservableCollection<FavoriteServerOption> FavoriteServers { get; } = [];
    public ObservableCollection<ProfileThemeItemViewModel> MyThemes { get; } = [];
    public ObservableCollection<ProfileThemeItemViewModel> FoundThemes { get; } = [];
    public override string Name => "Профиль";
    // Lucide user-round.
    public override string IconData => "M20,21 A8,8 0 0 0 4,21 M12,13 A5,5 0 1 0 12,3 A5,5 0 1 0 12,13";
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Username => _main.ActiveAccount?.Username ?? "Не выполнен вход";
    public bool IsCreator => _main.ActiveAccount?.UserId == CreatorId;
    public string UserId => _main.ActiveAccount?.UserId.ToString("D") ?? "-";
    public string TotalPlaytime => FormatDuration(TimeSpan.FromSeconds(PlaytimeTracker.GetAll().Sum(x => x.Duration.TotalSeconds)));
    public int ServersPlayed => PlaytimeTracker.GetAll().Count;
    public Bitmap? Avatar { get => _avatar; private set => SetProperty(ref _avatar, value); }
    public Bitmap? Banner { get => _banner; private set => SetProperty(ref _banner, value); }
    public Bitmap? FoundAvatar { get => _foundAvatar; private set => SetProperty(ref _foundAvatar, value); }
    public Bitmap? FoundBanner { get => _foundBanner; private set => SetProperty(ref _foundBanner, value); }
    public string Description { get => _description; set { if (value.Length <= 240) { SetProperty(ref _description, value); OnPropertyChanged(nameof(DescriptionCounter)); OnPropertyChanged(nameof(DescriptionDisplay)); } } }
    public string DescriptionCounter => $"{Description.Length}/240";
    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "Описание пока не заполнено." : Description;
    public FavoriteServerOption? SelectedFavoriteServer { get => _favoriteServer; set => SetProperty(ref _favoriteServer, value); }
    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public OrbitraProfileDto? FoundProfile { get => _found; private set { SetProperty(ref _found, value); OnPropertyChanged(nameof(HasFoundProfile)); OnPropertyChanged(nameof(FoundServerText)); OnPropertyChanged(nameof(FoundPresenceText)); OnPropertyChanged(nameof(FoundIsOnline)); OnPropertyChanged(nameof(FoundDescriptionDisplay)); OnPropertyChanged(nameof(FoundProfileIsCreator)); } }
    public bool FoundProfileIsCreator => FoundProfile?.UserId == CreatorId;
    public bool HasFoundProfile => FoundProfile != null && _showFoundProfileInSearch;
    public string FoundServerText => VisibleServer(FoundProfile);
    public string FoundPresenceText => "Онлайн";
    public bool FoundIsOnline => IsProfileOnline(FoundProfile);
    public string FoundDescriptionDisplay => string.IsNullOrWhiteSpace(FoundProfile?.Description) ? "Пользователь пока ничего о себе не рассказал." : FoundProfile.Description;
    public string OwnPresenceText => "Онлайн";
    public bool OwnIsOnline => !IsIncognito;
    public bool IsIncognito
    {
        get => _main.Cfg.GetCVar(CVars.OrbitraProfileStatus) == "invisible";
        set
        {
            _main.Cfg.SetCVar(CVars.OrbitraProfileStatus, value ? "invisible" : "online");
            _main.Cfg.CommitConfig();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OwnPresenceText));
            OnPropertyChanged(nameof(OwnIsOnline));
            OrbitraProtocol.PublishPresence(OrbitraProtocol.ActiveServer);
            _ = SaveSharingAsync(ShareCurrentServer);
        }
    }
    public bool ShareCurrentServer
    {
        get => _main.Cfg.GetCVar(CVars.OrbitraShareCurrentServer);
        set
        {
            _main.Cfg.SetCVar(CVars.OrbitraShareCurrentServer, value); _main.Cfg.CommitConfig();
            _ = SaveSharingAsync(value);
        }
    }

    public ProfileTabViewModel(MainWindowViewModel main) { _main = main; PlaytimeTracker.Changed += OnPlaytimeChanged; _socialPollTimer=new Timer(_=>PollSocialAsync(),null,TimeSpan.FromSeconds(8),TimeSpan.FromSeconds(15)); }
    public override async void Selected() => await RefreshAsync();

    public void OpenProfileSettings()
    {
        if (_main.Control == null) return;
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        _settingsWindow = new ProfileSettingsWindow { DataContext = this };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show(_main.Control);
    }

    public async void Refresh() => await RefreshAsync();
    private async Task RefreshAsync()
    {
        var account = _main.ActiveAccount;
        OnPropertyChanged(nameof(Username)); OnPropertyChanged(nameof(IsCreator)); OnPropertyChanged(nameof(UserId)); OnPropertyChanged(nameof(TotalPlaytime)); OnPropertyChanged(nameof(ServersPlayed));
        if (account == null) { Status = "Войдите в аккаунт SS14, чтобы открыть профиль."; return; }
        Busy = true;
        try
        {
            await _social.UpsertProfileAsync(account.UserId, account.Username, ShareCurrentServer, IsIncognito ? "invisible" : "online");
            var profile = await _social.GetProfileAsync(account.UserId);
            await LoadAvatarAsync(_social.AvatarUrl(profile?.AvatarPath));
            await LoadBannerAsync(_social.ProfileMediaUrl(profile?.BannerPath));
            Description = profile?.Description ?? "";
            OnPropertyChanged(nameof(DescriptionDisplay));
            RefreshFavoriteServers(profile?.FavoriteServer, profile?.FavoriteServerName);
            var friends = await _social.GetFriendsAsync(account.UserId);
            Friends.Clear(); foreach (var friend in friends) Friends.Add(new(this, friend));
            await LoadThemesAsync(account.UserId, MyThemes);
            Status = $"Друзей: {Friends.Count(x => x.IsAccepted)} · входящих заявок: {Friends.Count(x => x.IsIncoming)}";
        }
        catch (Exception e) { Status = e.Message; }
        finally { Busy = false; }
    }

    public async void ChooseAvatar()
    {
        var account = _main.ActiveAccount; var window = _main.Control; if (account == null || window == null) return;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title="Выберите аватар", AllowMultiple=false, FileTypeFilter=[new FilePickerFileType("Изображение") { Patterns=["*.png","*.jpg","*.jpeg"] }] });
        var file = files.FirstOrDefault(); if (file == null) return;
        try
        {
            await using var input = await file.OpenReadAsync(); using var memory = new MemoryStream(); await input.CopyToAsync(memory);
            var cropped = await new ImageCropWindow(memory.ToArray(), false).ShowDialog<byte[]?>(window); if (cropped == null) return;
            var url = await _social.UploadAvatarAsync(account.UserId, cropped, "image/png"); await LoadAvatarAsync(url);
            DiscordRichPresenceService.Instance.RefreshProfileAvatar();
            _main.ShowToast("Аватар профиля обновлён");
        }
        catch (Exception e) { _main.ShowToast(e.Message, true); }
    }

    public async void ChooseBanner()
    {
        var account = _main.ActiveAccount; var window = _main.Control; if (account == null || window == null) return;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title="Выберите баннер", AllowMultiple=false, FileTypeFilter=[new FilePickerFileType("Изображение") { Patterns=["*.png","*.jpg","*.jpeg"] }] });
        var file = files.FirstOrDefault(); if (file == null) return;
        try
        {
            await using var input = await file.OpenReadAsync(); using var memory = new MemoryStream(); await input.CopyToAsync(memory);
            var cropped = await new ImageCropWindow(memory.ToArray(), true).ShowDialog<byte[]?>(window); if (cropped == null) return;
            await LoadBannerAsync(await _social.UploadBannerAsync(account.UserId, cropped, "image/png"));
            _main.ShowToast("Баннер профиля обновлён");
        }
        catch (Exception e) { _main.ShowToast(e.Message, true); }
    }

    public async void SaveProfileDetails()
    {
        var account = _main.ActiveAccount; if (account == null) return;
        try
        {
            await _social.UpdateProfileDetailsAsync(account.UserId, Description,
                SelectedFavoriteServer?.Address, SelectedFavoriteServer?.Name);
            _main.ShowToast("Профиль сохранён");
        }
        catch (Exception e) { _main.ShowToast(e.Message, true); }
    }

    public async void FindUser()
    {
        if (string.IsNullOrWhiteSpace(Search)) return;
        Busy = true;
        try
        {
            _showFoundProfileInSearch = true;
            FoundProfile = await _social.FindProfileAsync(Search);
            OnPropertyChanged(nameof(HasFoundProfile));
            await LoadFoundImagesAsync(FoundProfile);
            await LoadThemesAsync(FoundProfile?.UserId, FoundThemes);
            Status = FoundProfile == null ? "Пользователь не найден." : "Профиль найден.";
        }
        catch (Exception e) { Status = e.Message; }
        finally { Busy = false; }
    }
    public void OpenFoundProfile()
    {
        if(FoundProfile==null||_main.Control==null)return;
        _profileWindow?.Close();
        _profileWindow=new OrbitraProfileWindow{DataContext=this};
        _profileWindow.Closed+=(_,_)=>_profileWindow=null;
        _profileWindow.Show(_main.Control);
    }
    internal async void ViewProfile(OrbitraFriendItemViewModel item)
    {
        Busy=true;
        try{_showFoundProfileInSearch=false;FoundProfile=await _social.GetProfileAsync(item.UserId);OnPropertyChanged(nameof(HasFoundProfile));await LoadFoundImagesAsync(FoundProfile);await LoadThemesAsync(FoundProfile?.UserId,FoundThemes);OpenFoundProfile();}
        catch(Exception e){_main.ShowToast(e.Message,true);}finally{Busy=false;}
    }
    public async void AddFoundFriend()
    {
        var me = _main.ActiveAccount; if (me == null || FoundProfile == null) return;
        try { await _social.SendFriendRequestAsync(me.UserId, FoundProfile.UserId); _main.ShowToast("Заявка в друзья отправлена"); await RefreshAsync(); }
        catch (Exception e) { _main.ShowToast(e.Message, true); }
    }
    internal async void Accept(OrbitraFriendItemViewModel item)
    { var me=_main.ActiveAccount; if(me==null)return; try { await _social.AcceptFriendAsync(me.UserId,item.UserId); await RefreshAsync(); } catch(Exception e){_main.ShowToast(e.Message,true);} }
    internal async void Remove(OrbitraFriendItemViewModel item)
    { var me=_main.ActiveAccount; if(me==null)return; try { await _social.RemoveFriendAsync(me.UserId,item.UserId); await RefreshAsync(); } catch(Exception e){_main.ShowToast(e.Message,true);} }
    internal void Connect(OrbitraFriendItemViewModel item) { if(item.CanConnect) ConnectingViewModel.StartConnect(_main,item.ServerAddress!); }
    internal async void Invite(OrbitraFriendItemViewModel item)
    { var me=_main.ActiveAccount;var server=OrbitraProtocol.ActiveServer;if(me==null||server==null)return;try{await _social.SendInviteAsync(me.UserId,item.UserId,server,server);_main.ShowToast($"Приглашение отправлено: {item.Username}");}catch(Exception e){_main.ShowToast(e.Message,true);} }
    internal async void CopyInvite(OrbitraFriendItemViewModel item)
    {
        if (!item.CanConnect || _main.Control?.Clipboard == null) return;
        await _main.Control.Clipboard.SetTextAsync(OrbitraProtocol.CreateInvite(item.ServerAddress!)); _main.ShowToast("Ссылка-приглашение скопирована");
    }
    private async Task SaveSharingAsync(bool enabled)
    {
        var account=_main.ActiveAccount; if(account==null)return;
        try { await _social.UpsertProfileAsync(account.UserId,account.Username,enabled,IsIncognito ? "invisible" : "online"); if(!enabled||IsIncognito) await _social.UpdatePresenceAsync(account.UserId,false,null,null); }
        catch(Exception e){_main.ShowToast(e.Message,true);}
    }
    private async Task LoadAvatarAsync(string? url)
    {
        Avatar?.Dispose(); Avatar=null; if(string.IsNullOrWhiteSpace(url))return;
        try { using var http=new System.Net.Http.HttpClient(); var bytes=await http.GetByteArrayAsync(url); Avatar=new Bitmap(new MemoryStream(bytes)); } catch { }
    }
    private async Task LoadBannerAsync(string? url)
    {
        Banner?.Dispose(); Banner=null; if(string.IsNullOrWhiteSpace(url))return;
        try { using var http=new System.Net.Http.HttpClient(); var bytes=await http.GetByteArrayAsync(url); Banner=new Bitmap(new MemoryStream(bytes)); } catch { }
    }
    private void RefreshFavoriteServers(string? selectedAddress, string? selectedName)
    {
        FavoriteServers.Clear();
        FavoriteServers.Add(new FavoriteServerOption(null, "Не выбран"));
        foreach (var favorite in _main.Cfg.FavoriteServers.Items.OrderBy(x => x.Name ?? x.Address))
            FavoriteServers.Add(new FavoriteServerOption(favorite.Address, favorite.Name ?? favorite.Address));
        SelectedFavoriteServer = FavoriteServers.FirstOrDefault(x => string.Equals(x.Address, selectedAddress, StringComparison.OrdinalIgnoreCase));
        if (SelectedFavoriteServer == null && !string.IsNullOrWhiteSpace(selectedAddress))
        {
            SelectedFavoriteServer = new FavoriteServerOption(selectedAddress, selectedName ?? selectedAddress);
            FavoriteServers.Add(SelectedFavoriteServer);
        }
        SelectedFavoriteServer ??= FavoriteServers[0];
    }
    private async Task LoadFoundImagesAsync(OrbitraProfileDto? profile)
    {
        FoundAvatar?.Dispose();FoundBanner?.Dispose();FoundAvatar=null;FoundBanner=null;if(profile==null)return;
        using var http=new System.Net.Http.HttpClient();
        try{var url=_social.AvatarUrl(profile.AvatarPath);if(url!=null)FoundAvatar=new Bitmap(new MemoryStream(await http.GetByteArrayAsync(url)));}catch{}
        try{var url=_social.ProfileMediaUrl(profile.BannerPath);if(url!=null)FoundBanner=new Bitmap(new MemoryStream(await http.GetByteArrayAsync(url)));}catch{}
    }
    private async Task LoadThemesAsync(Guid? userId, ObservableCollection<ProfileThemeItemViewModel> target)
    {
        target.Clear();
        if (userId == null) return;
        try
        {
            var themes = await _workshop.GetThemesAsync(_main.ActiveAccount?.UserId);
            foreach (var theme in themes.Where(x => x.AuthorUserId == userId.Value))
                target.Add(new ProfileThemeItemViewModel(this, theme));
        }
        catch { }
    }
    internal void OpenThemeWorkshop() => _main.CustomThemeTab.OpenWorkshop();
    internal void OpenThemeWorkshop(Guid themeId) => _main.CustomThemeTab.OpenWorkshopThemeById(themeId);
    internal void EditThemeWorkshop(Guid themeId) => _main.CustomThemeTab.EditWorkshopThemeById(themeId);
    internal bool IsOwnTheme(Guid authorId) => _main.ActiveAccount?.UserId == authorId;
    private void OnPlaytimeChanged(string _) { OnPropertyChanged(nameof(TotalPlaytime)); OnPropertyChanged(nameof(ServersPlayed)); }
    private static string VisibleServer(OrbitraProfileDto? p) =>
        IsProfileOnline(p) && p is { ShareCurrentServer:true, CurrentServer.Length:>0 }
            ? p.CurrentServerName ?? p.CurrentServer
            : IsProfileOnline(p) ? "В лаунчере" : "";
    private static bool IsProfileOnline(OrbitraProfileDto? profile) =>
        profile is { ProfileStatus: "online", PresenceUpdatedAt: { } updated } &&
        updated > DateTimeOffset.UtcNow.AddMinutes(-2);
    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? $"{(int)value.TotalHours} ч {value.Minutes} мин" : $"{Math.Max(0,value.Minutes)} мин";
    private async void PollSocialAsync()
    {
        var me=_main.ActiveAccount;if(me==null)return;
        try
        {
            var friends=await _social.GetFriendsAsync(me.UserId);
            var incoming=friends.Where(x=>x.IsIncoming).Select(x=>x.Profile).ToArray();
            if(_pollInitialized) foreach(var profile in incoming.Where(x=>!_knownIncoming.Contains(x.UserId)))
                SystemNotificationService.Show("Новая заявка в друзья",profile.Username,open:()=>_main.SelectProfileTab());
            _knownIncoming.Clear();foreach(var profile in incoming)_knownIncoming.Add(profile.UserId);
            var invites=await _social.GetInvitesAsync(me.UserId);
            foreach(var invite in invites)
            {
                var sender=await _social.GetProfileAsync(invite.SenderId);await _social.MarkInviteSeenAsync(invite.Id);
                SystemNotificationService.Show("Приглашение на сервер",$"{sender?.Username??"Пользователь"} приглашает на {invite.ServerName??invite.ServerAddress}",connect:()=>ConnectingViewModel.StartConnect(_main,invite.ServerAddress),open:()=>_main.SelectProfileTab());
            }
            _pollInitialized=true;
        }
        catch { }
    }
    public void Dispose() { _socialPollTimer.Dispose(); PlaytimeTracker.Changed -= OnPlaytimeChanged; Avatar?.Dispose(); Banner?.Dispose(); FoundAvatar?.Dispose(); FoundBanner?.Dispose(); _social.Dispose(); _workshop.Dispose(); }
}

public sealed class OrbitraFriendItemViewModel(ProfileTabViewModel owner, OrbitraFriendDto data)
{
    public Guid UserId => data.Profile.UserId; public string Username => data.Profile.Username;
    public bool IsCreator => UserId == ProfileTabViewModel.CreatorId;
    public bool IsIncoming => data.IsIncoming; public bool IsAccepted => data.Status == "accepted";
    public string State => IsIncoming ? "Входящая заявка" : IsAccepted ? "В друзьях" : "Заявка отправлена";
    public bool IsOnline => data.Profile.ProfileStatus == "online" && data.Profile.PresenceUpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-2);
    public string ProfileStatus => "Онлайн";
    public string? ServerAddress => data.Profile.ShareCurrentServer &&
        data.Profile.PresenceUpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-2) ? data.Profile.CurrentServer : null;
    public bool CanConnect => IsAccepted && !string.IsNullOrWhiteSpace(ServerAddress);
    public bool CanInvite => IsAccepted && OrbitraProtocol.ActiveServer != null;
    public string ServerText => CanConnect ? data.Profile.CurrentServerName ?? ServerAddress! : "Не играет или сервер скрыт";
    public void Accept() => owner.Accept(this); public void Remove() => owner.Remove(this);
    public void ViewProfile()=>owner.ViewProfile(this); public void Connect() => owner.Connect(this); public void Invite() => owner.Invite(this); public void CopyInvite() => owner.CopyInvite(this);
}
public sealed record FavoriteServerOption(string? Address,string Name){public override string ToString()=>Name;}
public sealed class ProfileThemeItemViewModel(ProfileTabViewModel owner, WorkshopThemeDto theme)
{
    public string Name => theme.Name;
    public string Description => string.IsNullOrWhiteSpace(theme.Description) ? "Без описания" : theme.Description;
    public string Meta => $"v{theme.Version} · {theme.LikeCount} ♥ · {theme.Downloads} загрузок";
    public bool IsOwn => owner.IsOwnTheme(theme.AuthorUserId);
    public void Open() => owner.OpenThemeWorkshop(theme.Id);
    public void Edit() => owner.EditThemeWorkshop(theme.Id);
}
