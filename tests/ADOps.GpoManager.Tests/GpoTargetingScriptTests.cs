using ADOps.GpoManager.ActiveDirectory;
using ADOps.GpoManager.Core.Commands;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Vérifie le contrat de filtrage AD DS utilisé pour déployer les modèles :
/// lecture pour « Domain Computers », application explicite pour la portée
/// Ordinateur, et exclusion des administrateurs pour la portée Utilisateur.
/// </summary>
public class GpoTargetingScriptTests
{
    private static readonly Guid GpoId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly string[] Members =
    {
        "CN=Henry,OU=Utilisateurs,DC=sarytany,DC=local",
        "CN=Jaona,OU=Utilisateurs,DC=sarytany,DC=local",
    };

    [Fact]
    public void ComputerScope_GrantsReadToUsersAndApplyToDomainComputers()
    {
        var command = new SetGpoTargetingCommand(
            GpoId,
            "ADOps-Computer-0001",
            "Ciblage ordinateur",
            Members,
            applyToComputers: true);

        var script = command.BuildScript();

        // Domain Computers est résolu par SID (RID 515) pour rester valable
        // sur un contrôleur de domaine localisé.
        Assert.Contains("-515", script);
        Assert.Contains("Domain Computers", script);
        Assert.Contains("GpoRead", script);
        Assert.Contains("GpoApply", script);

        // Le groupe de filtrage (utilisateurs ciblés) ne reçoit que la lecture
        // pour une GPO ordinateur : l'application est portée par Domain Computers.
        Assert.Contains(
            "Set-GPPermission -Guid $gpo.Id -TargetName 'ADOps-Computer-0001' -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction Stop",
            script);

        // Les administrateurs conservent l'édition mais jamais GpoApply (RID 512).
        Assert.Contains("-512", script);
        Assert.Contains("PermissionLevel GpoEditDeleteModifySecurity -Replace", script);

        // Sans -Replace, Set-GPPermission ne rétrograde pas GpoApply : le
        // filtrage resterait appliqué à tous les comptes authentifiés.
        Assert.Contains("PermissionLevel GpoRead -Replace", script);
        Assert.Contains("ApplyToComputers = $true", script);
    }

    [Fact]
    public void UserScope_GrantsApplyToTargetGroupAndKeepsAdministratorsReadOnly()
    {
        var command = new SetGpoTargetingCommand(
            GpoId,
            "ADOps-User-0002",
            "Ciblage utilisateur",
            Members,
            applyToComputers: false);

        var script = command.BuildScript();

        Assert.Contains(
            "Set-GPPermission -Guid $gpo.Id -TargetName 'ADOps-User-0002' -TargetType Group -PermissionLevel GpoApply -Replace -ErrorAction Stop",
            script);
        Assert.Contains("-515", script);
        Assert.Contains("-512", script);
        Assert.Contains("PermissionLevel GpoEditDeleteModifySecurity -Replace", script);
        Assert.Contains("ApplyToComputers = $false", script);
    }

    [Fact]
    public void UserScope_DoesNotGrantApplyToDomainComputers()
    {
        var command = new SetGpoTargetingCommand(
            GpoId,
            "ADOps-User-0003",
            "Ciblage utilisateur",
            Members,
            applyToComputers: false);

        var script = command.BuildScript();

        // Une seule occurrence de GpoApply : celle du groupe de filtrage.
        Assert.Equal(1, CountOccurrences(script, "PermissionLevel GpoApply"));
    }

    [Fact]
    public void LoopbackMerge_IsAddedOnceWithMergeMode()
    {
        var settings = new[]
        {
            new GpoRegistrySetting
            {
                Key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}",
                ValueName = "Deny_All",
                ValueType = "DWord",
                Value = "1",
            },
        };

        var once = GpoTemplateDeploymentService.WithLoopbackMerge(settings);
        var twice = GpoTemplateDeploymentService.WithLoopbackMerge(once);

        var loopback = Assert.Single(twice, setting => setting.ValueName == "UserPolicyMode");
        Assert.Equal(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", loopback.Key);
        Assert.Equal("DWord", loopback.ValueType);
        // 1 = Merge, 2 = Replace.
        Assert.Equal("1", loopback.Value);
        Assert.Equal(settings.Length + 1, twice.Count);
    }

    [Fact]
    public void LoopbackMerge_DoesNotOverrideAnExplicitValue()
    {
        var settings = new[]
        {
            new GpoRegistrySetting
            {
                Key = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
                ValueName = "UserPolicyMode",
                ValueType = "DWord",
                Value = "2",
            },
        };

        var result = GpoTemplateDeploymentService.WithLoopbackMerge(settings);

        var loopback = Assert.Single(result);
        Assert.Equal("2", loopback.Value);
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }
}
