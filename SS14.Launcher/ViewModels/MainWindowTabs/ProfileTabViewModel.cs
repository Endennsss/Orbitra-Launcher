using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using SS14.Launcher.Models.Data;
using SS14.Launcher.Views;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class ProfileTabViewModel : MainWindowTabViewModel, IDisposable
{
    private readonly MainWindowViewModel _main;
    private readonly OrbitraSocialService _social = new();
    private bool _busy;
    private string _status = "";
    private string _search = "";
    private string _reportReason = "";
    private OrbitraProfileDto? _found;
    private Bitmap? _avatar;
    public ObservableCollection<OrbitraFriendItemViewModel> Friends { get; } = [];
    public override string Name => "Профиль";
    // Lucide user-round.
    public override string IconData => "M18,20 A6,6 0 0 0 6,20 M12,12 A4,4 0 1 0 12,4 A4,4 0 1 0 12,12";
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Username => _main.ActiveAccount?.Username ?? "Не выполнен вход";
    public string UserId => _main.ActiveAccount?.UserId.ToString("D") ?? "—";
    public string TotalPlaytime => FormatDuration(TimeSpan.FromSeconds(PlaytimeTracker.GetAll().Sum(x => x.Duration.TotalSeconds)));
    public int ServersPlayed => PlaytimeTracker.GetAll().Count;
    public Bitmap? Avatar { get => _avatar; private set => SetProperty(ref _avatar, value); }
    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public string ReportReason { get => _reportReason; set => SetProperty(ref _reportReason, value); }
    public OrbitraProfileDto? FoundProfile { get => _found; private set { SetProperty(ref _found, value); OnPropertyChanged(nameof(HasFoundProfile)); OnPropertyChanged(nameof(FoundServerText)); } }
    public bool HasFoundProfile => FoundProfile != null;
    public string FoundServerText => VisibleServer(FoundProfile);
    public bool ShareCurrentServer
    {
        get => _main.Cfg.GetCVar(CVars.OrbitraShareCurrentServer);
        set
        {
            _main.Cfg.SetCVar(CVars.OrbitraShareCurrentServer, value); _main.Cfg.CommitConfig();
            _ = SaveSharingAsync(value);
        }
    }

    public ProfileTabViewModel(MainWindowViewModel main) { _main = main; PlaytimeTracker.Changed += OnPlaytimeChanged; }
    public override async void Selected() => await RefreshAsync();

    public async void Refresh() => await RefreshAsync();
    private async Task RefreshAsync()
    {
        var account = _main.ActiveAccount;
        OnPropertyChanged(nameof(Username)); OnPropertyChanged(nameof(UserId)); OnPropertyChanged(nameof(TotalPlaytime)); OnPropertyChanged(nameof(ServersPlayed));
        if (account == null) { Status = "Войдите в аккаунт SS14, чтобы открыть профиль."; return; }
        Busy = true;
        try
        {
            await _social.UpsertProfileAsync(account.UserId, account.Username, ShareCurrentServer);
            var profile = await _social.GetProfileAsync(account.UserId);
            await LoadAvatarAsync(_social.AvatarUrl(profile?.AvatarPath));
            var friends = await _social.GetFriendsAsync(account.UserId);
            Friends.Clear(); foreach (var friend in friends) Friends.Add(new(this, friend));
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
            var ext = Path.GetExtension(file.Name).ToLowerInvariant(); var mime = ext == ".png" ? "image/png" : "image/jpeg";
            var url = await _social.UploadAvatarAsync(account.UserId, memory.ToArray(), mime); await LoadAvatarAsync(url);
            _main.ShowToast("Аватар профиля обновлён");
        }
        catch (Exception e) { _main.ShowToast(e.Message, true); }
    }

    public async void FindUser()
    {
        if (string.IsNullOrWhiteSpace(Search)) return;
        Busy = true;
        try { FoundProfile = await _social.FindProfileAsync(Search); Status = FoundProfile == null ? "Пользователь не найден." : "Профиль найден."; }
        catch (Exception e) { Status = e.Message; }
        finally { Busy = false; }
    }
    public async void AddFoundFriend()
    {
        var me = _main.ActiveAccount; if (me == null || FoundProfile == null) return;
        try { await _social.SendFriendRequestAsync(me.UserId, FoundProfile.UserId); _main.ShowToast("Заявка в друзья отправлена"); await RefreshAsync(); }
        catch (Exception e) { _main.ShowToast(e.Message, true); }
    }
    public async void ReportFoundUser()
    {
        var me = _main.ActiveAccount; if (me == null || FoundProfile == null) return;
        if (ReportReason.Trim().Length < 5) { _main.ShowToast("Опишите причину жалобы", true); return; }
        try { await _social.ReportAsync(me.UserId, FoundProfile.UserId, ReportReason); ReportReason=""; _main.ShowToast("Жалоба отправлена"); }
        catch (Exception e) { _main.ShowToast(e.Message, true); }
    }
    internal async void Accept(OrbitraFriendItemViewModel item)
    { var me=_main.ActiveAccount; if(me==null)return; try { await _social.AcceptFriendAsync(me.UserId,item.UserId); await RefreshAsync(); } catch(Exception e){_main.ShowToast(e.Message,true);} }
    internal async void Remove(OrbitraFriendItemViewModel item)
    { var me=_main.ActiveAccount; if(me==null)return; try { await _social.RemoveFriendAsync(me.UserId,item.UserId); await RefreshAsync(); } catch(Exception e){_main.ShowToast(e.Message,true);} }
    internal void Connect(OrbitraFriendItemViewModel item) { if(item.CanConnect) ConnectingViewModel.StartConnect(_main,item.ServerAddress!); }
    internal async void CopyInvite(OrbitraFriendItemViewModel item)
    {
        if (!item.CanConnect || _main.Control?.Clipboard == null) return;
        await _main.Control.Clipboard.SetTextAsync(OrbitraProtocol.CreateInvite(item.ServerAddress!)); _main.ShowToast("Ссылка-приглашение скопирована");
    }
    private async Task SaveSharingAsync(bool enabled)
    {
        var account=_main.ActiveAccount; if(account==null)return;
        try { await _social.UpsertProfileAsync(account.UserId,account.Username,enabled); if(!enabled) await _social.UpdatePresenceAsync(account.UserId,false,null,null); }
        catch(Exception e){_main.ShowToast(e.Message,true);}
    }
    private async Task LoadAvatarAsync(string? url)
    {
        Avatar?.Dispose(); Avatar=null; if(string.IsNullOrWhiteSpace(url))return;
        try { using var http=new System.Net.Http.HttpClient(); var bytes=await http.GetByteArrayAsync(url); Avatar=new Bitmap(new MemoryStream(bytes)); } catch { }
    }
    private void OnPlaytimeChanged(string _) { OnPropertyChanged(nameof(TotalPlaytime)); OnPropertyChanged(nameof(ServersPlayed)); }
    private static string VisibleServer(OrbitraProfileDto? p) =>
        p is { ShareCurrentServer:true, CurrentServer.Length:>0 } && p.PresenceUpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-2)
            ? p.CurrentServerName ?? p.CurrentServer
            : "Сервер скрыт или пользователь не играет";
    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1 ? $"{(int)value.TotalHours} ч {value.Minutes} мин" : $"{Math.Max(0,value.Minutes)} мин";
    public void Dispose() { PlaytimeTracker.Changed -= OnPlaytimeChanged; Avatar?.Dispose(); _social.Dispose(); }
}

public sealed class OrbitraFriendItemViewModel(ProfileTabViewModel owner, OrbitraFriendDto data)
{
    public Guid UserId => data.Profile.UserId; public string Username => data.Profile.Username;
    public bool IsIncoming => data.IsIncoming; public bool IsAccepted => data.Status == "accepted";
    public string State => IsIncoming ? "Входящая заявка" : IsAccepted ? "В друзьях" : "Заявка отправлена";
    public string? ServerAddress => data.Profile.ShareCurrentServer &&
        data.Profile.PresenceUpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-2) ? data.Profile.CurrentServer : null;
    public bool CanConnect => IsAccepted && !string.IsNullOrWhiteSpace(ServerAddress);
    public string ServerText => CanConnect ? data.Profile.CurrentServerName ?? ServerAddress! : "Не играет или сервер скрыт";
    public void Accept() => owner.Accept(this); public void Remove() => owner.Remove(this);
    public void Connect() => owner.Connect(this); public void CopyInvite() => owner.CopyInvite(this);
}
