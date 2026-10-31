namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Contexte d'audit enrichi (cahier des charges §12) : auteur, serveur, domaine, objet/GUID,
/// cibles, état initial/connu, état demandé, résultat, horodatage UTC, durée, code d'erreur, identifiant d'opération.
/// </summary>
public sealed record AuditContext(
    string? Server = null,
    string? Domain = null,
    string? ObjectGuid = null,
    string? Targets = null,
    string? InitialState = null,
    string? RequestedState = null,
    TimeSpan? Duration = null,
    string? ErrorCode = null,
    string? OperationId = null);
