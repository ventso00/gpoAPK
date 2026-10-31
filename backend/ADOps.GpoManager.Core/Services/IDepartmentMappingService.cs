using ADOps.GpoManager.Core.Models;

namespace ADOps.GpoManager.Core.Services;

/// <summary>
/// Disponibilité des ordinateurs du parc. Méthodes multiples (DNS, WinRM) :
/// un ping échoué ne prouve pas qu'une machine est éteinte.
/// </summary>
public interface IComputerAvailabilityService
{
    Task<IReadOnlyList<ComputerHealthInfo>> CheckAsync(
        IReadOnlyList<ComputerInfo> computers,
        DomainContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Mapping département ↔ OU (configuration locale, AD DS jamais modifié).</summary>
public interface IDepartmentMappingService
{
    Task<IReadOnlyList<DepartmentMapping>> GetMappingsAsync();
    Task SaveMappingAsync(string departmentName, string? ouDistinguishedName);
    Task RemoveMappingAsync(string departmentName);
}
