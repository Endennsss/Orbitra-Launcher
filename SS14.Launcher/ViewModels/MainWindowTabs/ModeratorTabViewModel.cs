using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.Toolkit.Mvvm.ComponentModel;

namespace SS14.Launcher.ViewModels.MainWindowTabs;

public sealed class ModeratorTabViewModel : MainWindowTabViewModel, IDisposable
{
    public static readonly Guid ModeratorId = Guid.Parse("1d17bc4a-25fa-4ef9-af88-fb1d4dd71701");
    private readonly MainWindowViewModel _main;
    private readonly OrbitraSocialService _social = new();
    private string _status = "";
    public override string Name => "Модерация";
    public override string IconData => "M12,22 S20,18 20,12 V5 L12,2 L4,5 V12 C4,18 12,22 12,22 M9,12 L11,14 L15,10";
    public ObservableCollection<ModerationReportItemViewModel> Reports { get; } = [];
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public ModeratorTabViewModel(MainWindowViewModel main) => _main = main;
    public override async void Selected() => await RefreshAsync();
    public async void Refresh() => await RefreshAsync();
    private async Task RefreshAsync()
    {
        if (!_main.HasModeratorAccess) { Reports.Clear(); Status="Нет доступа."; return; }
        try { var rows=await _social.GetModerationReportsAsync(); Reports.Clear(); foreach(var row in rows) Reports.Add(new(this,row)); Status=$"Жалоб: {Reports.Count}"; }
        catch(Exception e){Status=e.Message;}
    }
    internal async void SetStatus(ModerationReportItemViewModel item,string status)
    { if(!_main.HasModeratorAccess)return;try{await _social.ResolveModerationReportAsync(item.Id,ModeratorId,status);await RefreshAsync();}catch(Exception e){_main.ShowToast(e.Message,true);} }
    public void Dispose()=>_social.Dispose();
}
public sealed class ModerationReportItemViewModel(ModeratorTabViewModel owner,OrbitraModerationReportDto report)
{
    public long Id=>report.Id; public string Type=>report.TargetType=="theme"?"Тема":"Игрок";
    public string Target=>report.TargetName??report.TargetId; public string Reason=>report.Reason;
    public string Reporter=>report.ReporterId.ToString("D"); public string Created=>report.CreatedAt.LocalDateTime.ToString("g");
    public string Status=>report.Status switch{"resolved"=>"Рассмотрено","rejected"=>"Отклонено",_=>"Открыто"};
    public bool IsOpen=>report.Status=="open"; public void Resolve()=>owner.SetStatus(this,"resolved"); public void Reject()=>owner.SetStatus(this,"rejected");
}
