using Microsoft.Extensions.DependencyInjection;
using ADOps.GpoManager.App.ViewModels;

namespace ADOps.GpoManager.App.Services;

public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public event EventHandler<ViewModelBase>? Navigated;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void NavigateTo<TViewModel>(Action<TViewModel>? initialize = null) where TViewModel : ViewModelBase
    {
        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        initialize?.Invoke(viewModel);
        Navigated?.Invoke(this, viewModel);
        viewModel.OnNavigatedTo();
    }
}
