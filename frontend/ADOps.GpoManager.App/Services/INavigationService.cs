using ADOps.GpoManager.App.ViewModels;

namespace ADOps.GpoManager.App.Services;

/// <summary>Navigation entre les pages : associe le ViewModel demandé au contenu de la fenêtre principale.</summary>
public interface INavigationService
{
    event EventHandler<ViewModelBase>? Navigated;
    void NavigateTo<TViewModel>(Action<TViewModel>? initialize = null) where TViewModel : ViewModelBase;
}
