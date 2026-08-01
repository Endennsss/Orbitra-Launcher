namespace SS14.Launcher.ViewModels.MainWindowTabs;

public abstract class MainWindowTabViewModel : ViewModelBase
{
    public abstract string Name { get; }
    public abstract string IconData { get; }
    public virtual string BadgeText => string.Empty;
    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    protected void BadgeChanged()
    {
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasBadge));
    }

    public bool IsSelected { get; set; }

    public virtual void Selected()
    {
    }
}
