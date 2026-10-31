using System.Windows;

namespace ADOps.GpoManager.App.Views;

/// <summary>
/// Fenêtre de verrouillage de session. Elle remplace la ré-authentification
/// (écran de connexion) supprimé : la session Windows courante est
/// l'identité, il n'y a donc plus de mot de passe à ressaisir.
/// Fermer la fenêtre sans déverrouiller équivaut à quitter l'application.
/// </summary>
public partial class SessionLockWindow : Window
{
    public SessionLockWindow()
    {
        InitializeComponent();
    }

    /// <summary>Vrai si l'utilisateur a demandé le déverrouillage.</summary>
    public bool IsUnlocked { get; private set; }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        IsUnlocked = true;
        DialogResult = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        IsUnlocked = false;
        DialogResult = false;
    }
}
