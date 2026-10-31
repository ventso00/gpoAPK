using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Models;

/// <summary>Contexte de connexion au domaine : serveur cible, domaine, mode d'exécution et identité.</summary>
public sealed class DomainContext
{
    public string ServerFqdn { get; }
    public string DomainFqdn { get; }
    public string? DomainNetbios { get; }
    public ExecutorMode ExecutorMode { get; }
    public string UserName { get; }
    public ConnectionCredentials Credentials { get; }
    public DateTime ConnectedAtUtc { get; }
    public HealthCheckReport? LastHealthCheck { get; }

    public DomainContext(
        string serverFqdn,
        string domainFqdn,
        string? domainNetbios,
        ExecutorMode executorMode,
        string userName,
        DateTime connectedAtUtc,
        ConnectionCredentials? credentials = null,
        HealthCheckReport? lastHealthCheck = null)
    {
        var (serverOk, serverError) = DomainValidator.ValidateServerFqdn(serverFqdn);
        if (!serverOk)
        {
            throw new ArgumentException(serverError, nameof(serverFqdn));
        }

        var (domainOk, domainError) = DomainValidator.ValidateDomainName(domainFqdn);
        if (!domainOk)
        {
            throw new ArgumentException(domainError, nameof(domainFqdn));
        }

        ServerFqdn = DomainValidator.NormalizeFqdn(serverFqdn);
        DomainFqdn = DomainValidator.NormalizeFqdn(domainFqdn);
        DomainNetbios = string.IsNullOrWhiteSpace(domainNetbios) ? null : domainNetbios.Trim();
        ExecutorMode = executorMode;
        UserName = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName;
        Credentials = credentials ?? new ConnectionCredentials();
        ConnectedAtUtc = connectedAtUtc;
        LastHealthCheck = lastHealthCheck;
    }
}
