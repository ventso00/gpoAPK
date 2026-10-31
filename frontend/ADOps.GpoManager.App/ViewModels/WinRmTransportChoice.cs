using ADOps.GpoManager.Core.Security;

namespace ADOps.GpoManager.App.ViewModels;

/// <summary>
/// Entrée de la liste déroulante « canal WinRM » : la valeur est celle
/// persistée dans settings.json, le libellé et l'explication sont destinés
/// à l'utilisateur (le code est en français, l'identifiant en anglais).
/// </summary>
public sealed record WinRmTransportChoice(WinRmTransportMode Value, string Label, string Description);