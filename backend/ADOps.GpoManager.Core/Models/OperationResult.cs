using System.Text;

namespace ADOps.GpoManager.Core.Models;

/// <summary>
/// Erreur structurée : titre, cause probable et étapes de résolution.
/// Contrainte du cahier des charges : jamais un simple « PowerShell exited with code 1 ».
/// </summary>
public sealed class OperationError
{
    public string Title { get; }
    public string? Cause { get; }
    public string? Remediation { get; }
    public string? RawError { get; }

    public OperationError(string title, string? cause = null, string? remediation = null, string? rawError = null)
    {
        Title = title;
        Cause = cause;
        Remediation = remediation;
        RawError = rawError;
    }

    public string ToDisplayString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Title);
        if (!string.IsNullOrWhiteSpace(Cause))
        {
            sb.AppendLine("Cause probable : " + Cause);
        }

        if (!string.IsNullOrWhiteSpace(Remediation))
        {
            sb.AppendLine("Résolution : " + Remediation);
        }

        return sb.ToString().TrimEnd();
    }
}

/// <summary>Résultat d'une opération : soit une valeur, soit une erreur explicite.</summary>
public sealed class OperationResult<T>
{
    public bool IsSuccess { get; }
    public T? Value { get; }
    public OperationError? Error { get; }

    private OperationResult(bool isSuccess, T? value, OperationError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public static OperationResult<T> Success(T value) => new(true, value, null);

    public static OperationResult<T> Failure(OperationError error) => new(false, default, error);
}
