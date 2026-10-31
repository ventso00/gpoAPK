namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Résultat de la détection automatique de la session (remplacement de
/// l'écran de connexion) : identité de la session, contexte de domaine
/// exploitable, rapport de santé et éventuels avertissements.
/// </summary>
public sealed class SessionBootstrapResult
{
    /// <summary>Vrai si un contexte de domaine exploitable a pu être construit.</summary>
    public bool Success { get; init; }

    /// <summary>Identité de la session Windows courante.</summary>
    public SessionIdentity Identity { get; init; } = new();

    /// <summary>Contexte de domaine construit (null si la détection a échoué).</summary>
    public DomainContext? Context { get; init; }

    /// <summary>Rapport de santé de la cible (null si le contrôle n'a pas pu être lancé).</summary>
    public HealthCheckReport? Report { get; init; }

    /// <summary>Cause de l'échec, en langage actionnable.</summary>
    public string? Error { get; init; }

    /// <summary>Avertissements non bloquants (session non élevée, DC de repli…).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>Synthèse affichable dans la barre d'état.</summary>
    public string Summary
    {
        get
        {
            if (Success && Context is not null)
            {
                return "Session implicite : " + Identity.DisplayText
                    + " (" + (Identity.IsElevated ? "élevé" : "non élevé") + ") — domaine "
                    + Context.DomainFqdn + " via " + Context.ServerFqdn;
            }

            return Error ?? "Aucune session de domaine exploitable n'a pu être détectée.";
        }
    }
}
