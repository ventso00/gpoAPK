using ADOps.GpoManager.PowerShell;
using ADOps.GpoManager.PowerShell.Host;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Tests de la traduction des erreurs PowerShell en erreurs explicites (contrainte n°4).</summary>
public class ErrorInterpreterTests
{
    private readonly ErrorInterpreter _interpreter = new();

    [Fact]
    public void Interpret_RpcUnavailable_ReturnsFirewallRemediation()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, "The RPC server is unavailable. (Exception from HRESULT: 0x800706BA)", false, TimeSpan.FromSeconds(2));
        var error = _interpreter.Interpret(result, "Liste des GPO");
        Assert.Contains("RPC", error.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pare-feu", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_WinRmNotConfigured_ReturnsWinRmRemediation()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, "The client cannot connect to the destination specified in the request. Verify that the service on the destination is running and is accepting requests.", false, TimeSpan.FromSeconds(3));
        var error = _interpreter.Interpret(result, "Service WinRM");
        Assert.Contains("WinRM", error.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("winrm quickconfig", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_AccessDenied_ReturnsPermissionsRemediation()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, "Access is denied. (Exception from HRESULT: 0x80070005 (E_ACCESSDENIED))", false, TimeSpan.FromSeconds(1));
        var error = _interpreter.Interpret(result, "Authentification");
        Assert.Contains("Accès refusé", error.Title);
        Assert.Contains("Remote Management Users", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_GetGpoNotRecognized_ReturnsRsatRemediation()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, "The term 'Get-GPO' is not recognized as a name of a cmdlet, function, script file, or operable program.", false, TimeSpan.FromSeconds(1));
        var error = _interpreter.Interpret(result, "Liste des GPO");
        Assert.Contains("GroupPolicy", error.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rsat.GroupPolicy.Management", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_DnsError_ReturnsDnsRemediation()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, "Resolve-DnsName : DNS name does not exist", false, TimeSpan.FromSeconds(1));
        var error = _interpreter.Interpret(result, "Résolution DNS");
        Assert.Contains("DNS", error.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nslookup", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_NetworkPathNotFound_ReturnsConnectivityRemediation()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, "The network path was not found. (Exception from HRESULT: 0x80070035 (ERROR_BAD_NETPATH))", false, TimeSpan.FromSeconds(1));
        var error = _interpreter.Interpret(result, "Service WinRM");
        Assert.Contains("Chemin réseau introuvable", error.Title);
        Assert.Contains("Test-Connection", error.Remediation ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Interpret_DomainControllerNotFound_ReturnsLocatorRemediation()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, "The specified domain does not exist or could not be contacted.", false, TimeSpan.FromSeconds(1));
        var error = _interpreter.Interpret(result, "Liste des GPO");
        Assert.Contains("Contrôleur de domaine introuvable", error.Title);
    }

    [Fact]
    public void Interpret_Timeout_ReturnsTimeoutError()
    {
        var result = new PowerShellExecutionResult(-1, string.Empty, string.Empty, TimedOut: true, TimeSpan.FromSeconds(120));
        var error = _interpreter.Interpret(result, "Test");
        Assert.Contains("Délai d'attente dépassé", error.Title);
    }

    [Fact]
    public void Interpret_UnknownError_ReturnsExitCodeAndRawOutput()
    {
        var result = new PowerShellExecutionResult(42, string.Empty, "Something weird happened", false, TimeSpan.FromSeconds(1));
        var error = _interpreter.Interpret(result, "Opération X");
        Assert.Contains("42", error.Title);
        Assert.Contains("Something weird happened", error.Cause ?? string.Empty);
        Assert.Equal("Something weird happened", error.RawError);
    }

    [Fact]
    public void Interpret_EmptyStderr_ReturnsGenericFallback()
    {
        var result = new PowerShellExecutionResult(1, string.Empty, string.Empty, false, TimeSpan.FromSeconds(1));
        var error = _interpreter.Interpret(result, "Opération");
        Assert.Contains("code de sortie 1", error.Title);
        Assert.Contains("(aucune sortie d'erreur)", error.Cause ?? string.Empty);
    }
}
