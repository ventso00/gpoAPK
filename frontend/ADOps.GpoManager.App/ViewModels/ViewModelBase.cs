using CommunityToolkit.Mvvm.ComponentModel;

namespace ADOps.GpoManager.App.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Appelé par le service de navigation lorsque la page devient active.</summary>
    public virtual void OnNavigatedTo()
    {
    }
}
