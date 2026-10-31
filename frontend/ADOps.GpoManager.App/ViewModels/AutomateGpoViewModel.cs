using System.Windows;
using ADOps.GpoManager.App.Services;
using ADOps.GpoManager.App.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Assistant « Automatiser la GPO » : guide l'administrateur en 3 étapes
/// (1. Machines cibles, 2. GPO, 3. Validation &amp; déploiement) au lieu de
/// l'obliger à naviguer manuellement entre Parc, GPO et Déploiements.
/// Les ViewModels existants sont réutilisés tels quels : aucun service
/// métier n'est dupliqué, seule la navigation est guidée.
/// </summary>
public sealed partial class AutomateGpoViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;

    public ParcViewModel Parc { get; }
    public GpoListViewModel Gpos { get; }
    public DeploymentsViewModel Deployments { get; }

    [ObservableProperty]
    private int _currentStep = 1;

    public string StepTitle => CurrentStep switch
    {
        1 => "Étape 1 — Sélection des machines cibles",
        2 => "Étape 2 — Choix ou ajout de GPO",
        _ => "Étape 3 — Validation et application",
    };

    public string StepHint => CurrentStep switch
    {
        1 => "Cochez les machines du domaine sur lesquelles appliquer les GPO, puis cliquez « Suivant ».",
        2 => "Sélectionnez des GPO existantes ou ajoutez-en une depuis le catalogue, puis cliquez « Suivant ».",
        _ => "Vérifiez le rapport d'impact, puis cliquez « Appliquer » pour déployer les liens.",
    };

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;

    public AutomateGpoViewModel(
        INavigationService navigation,
        ParcViewModel parc,
        GpoListViewModel gpos,
        DeploymentsViewModel deployments)
    {
        _navigation = navigation;
        Parc = parc;
        Gpos = gpos;
        Deployments = deployments;
    }

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        // Synchronise les données à chaque entrée dans l'assistant.
        Parc.OnNavigatedTo();
        Gpos.OnNavigatedTo();
        Deployments.OnNavigatedTo();
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < 3) { CurrentStep++; RaiseStepChanged(); }
    }

    [RelayCommand]
    private void PrevStep()
    {
        if (CurrentStep > 1) { CurrentStep--; RaiseStepChanged(); }
    }

    [RelayCommand]
    private void GoToStep1() { CurrentStep = 1; RaiseStepChanged(); }

    [RelayCommand]
    private void GoToStep2() { CurrentStep = 2; RaiseStepChanged(); }

    [RelayCommand]
    private void GoToStep3() { CurrentStep = 3; RaiseStepChanged(); }

    private void RaiseStepChanged()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(StepHint));
    }

    [RelayCommand]
    private void BackToDashboard() => _navigation.NavigateTo<DashboardViewModel>();
}
