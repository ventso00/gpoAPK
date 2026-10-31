using ADOps.GpoManager.ActiveDirectory;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Network;
using ADOps.GpoManager.Infrastructure.Services;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>
/// Session implicite (remplacement de l'écran de connexion) : détection de
/// l'identité de la session, du domaine et du contrôleur.
/// </summary>
public sealed class SessionBootstrapTests
{
    [Fact]
    public void MachineDomain_Detect_NeverThrows()
    {
        // La détection ne doit jamais lever : un poste hors domaine est un
        // cas normal, pas une exception.
        var info = MachineDomain.Detect();

        Assert.NotNull(info);
        Assert.NotNull(info.Source);
    }

    [Fact]
    public void MachineDomain_Unknown_IsNotDomainJoined()
    {
        var info = MachineDomain.Info.Unknown("test");

        Assert.False(info.IsDomainJoined);
        Assert.Null(info.DomainName);
        Assert.Null(info.DcFqdn);
    }

    [Fact]
    public void MachineDomain_Joined_RequiresAFullyQualifiedName()
    {
        var info = new MachineDomain.Info("lab.local", "dc1.lab.local", "192.168.1.10", true, "test");

        Assert.True(info.IsDomainJoined);
        Assert.Equal("dc1.lab.local", info.DcFqdn);
        Assert.Equal("192.168.1.10", info.DcAddress);
    }

    [Fact]
    public void SessionIdentity_Detect_ReturnsTheCurrentAccount()
    {
        // La détection de session est une API Windows : elle vit dans le projet
        // « net10.0-windows » (Infrastructure), jamais dans Core.
        var identity = WindowsSessionProbe.Detect();

        Assert.False(string.IsNullOrWhiteSpace(identity.UserName));
        Assert.NotNull(identity.ElevationText);
        Assert.NotNull(identity.DomainSource);
    }

    [Fact]
    public void SessionIdentity_DisplayText_UsesDomainQualifiedName()
    {
        var identity = new SessionIdentity
        {
            UserName = "bob",
            UserDomainName = "LAB",
            IsElevated = true,
        };

        Assert.Equal("LAB\\bob", identity.DisplayText);
        Assert.Equal("Élevé", identity.ElevationText);
    }

    [Fact]
    public void SessionIdentity_DisplayText_FallsBackToUserName()
    {
        var identity = new SessionIdentity { UserName = "bob" };

        Assert.Equal("bob", identity.DisplayText);
        Assert.Equal("Non élevé", identity.ElevationText);
    }

    [Fact]
    public void SessionBootstrapResult_Summary_DescribesTheSession()
    {
        var result = new SessionBootstrapResult
        {
            Success = true,
            Identity = new SessionIdentity { UserName = "bob", UserDomainName = "LAB", IsElevated = true },
            Context = new DomainContext(
                "dc1.lab.local",
                "lab.local",
                null,
                ExecutorMode.Local,
                "bob",
                DateTime.UtcNow),
        };

        Assert.Contains("Session implicite", result.Summary);
        Assert.Contains("lab.local", result.Summary);
        Assert.Contains("dc1.lab.local", result.Summary);
    }

    [Fact]
    public void SessionBootstrapResult_Summary_FallsBackToError()
    {
        var result = new SessionBootstrapResult { Success = false, Error = "poste hors domaine" };

        Assert.Equal("poste hors domaine", result.Summary);
    }

    [Fact]
    public void DcLocator_DiscoverPrimaryController_NeverThrows()
    {
        // Sur une machine non jointe, la découverte doit retourner null
        // (et non lever) : c'est le chemin normal de détection du poste
        // d'administration hors domaine.
        var discovered = DcLocator.DiscoverPrimaryController();

        if (discovered is not null)
        {
            Assert.Contains(".", discovered.Fqdn);
            // L'adresse est renvoyée sous forme UNC (« \\192.168.0.1 ») : elle
            // doit être normalisée pour être comparable à une adresse IP.
            Assert.DoesNotContain("\\", discovered.Address);
        }
    }

    [Fact]
    public void DcLocator_NormalizeAddress_RemovesUncPrefix()
    {
        Assert.Equal("192.168.5.2", DcLocator.NormalizeAddress("\\\\192.168.5.2"));
        Assert.Equal("192.168.5.2", DcLocator.NormalizeAddress("  192.168.5.2 "));
        Assert.Equal(string.Empty, DcLocator.NormalizeAddress(null));
    }
}

/// <summary>Collecte des comptes clients (utilisateurs) et leur classification.</summary>
public sealed class ClientAccountTests
{
    [Fact]
    public void ListUsersCommand_CollectsAccountState()
    {
        var command = new ListUsersCommand();

        Assert.Contains("Get-ADUser -Filter *", command.ScriptBody);
        Assert.Contains("AccountLocked", command.ScriptBody);
        Assert.Contains("PasswordNeverExpires", command.ScriptBody);
    }

    [Fact]
    public void ListUsersCommand_DoesNotRequestMemberOf()
    {
        // memberOf est une rétro-liaison : l'exiger pour chaque compte rendait
        // la synchronisation anormalement lente (symptôme « l'application
        // disparaît pendant la collecte »). La classification administrateur
        // passe désormais par ListDomainAdministratorsCommand.
        var command = new ListUsersCommand();

        Assert.DoesNotContain("memberOf", command.ScriptBody);
    }

    [Fact]
    public void ListDomainAdministratorsCommand_QueriesAdminGroups()
    {
        var command = new ListDomainAdministratorsCommand();

        Assert.Contains("Get-ADGroupMember", command.ScriptBody);
        Assert.Contains("Get-ADDomain", command.ScriptBody);
        // L'identifiant doit être entre parenthèses : en PowerShell,
        // « $sid-512 » serait une soustraction, pas une concaténation.
        Assert.Contains("\"$($domainSid)-$rid\"", command.ScriptBody);
        Assert.True(command.Timeout > TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ListUsersCommand_BuildsWellFormedScript()
    {
        var script = new ListUsersCommand().BuildScript();

        Assert.Contains("$ErrorActionPreference = 'Stop'", script);
        Assert.Contains("ConvertTo-Json -Depth 10 -Compress", script);
        Assert.Contains("Get-ADUser -Filter *", script);
    }

    [Fact]
    public void ListUsersCommand_HasExplicitTimeout()
    {
        Assert.True(new ListUsersCommand().Timeout > TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void UserInfo_AccountStatusText_PrioritisesDisabledThenLocked()
    {
        var disabledAdmin = new UserInfo
        {
            SamAccountName = "a",
            Enabled = false,
            AccountLocked = true,
            IsAdministrator = true,
        };
        Assert.Equal("Désactivé", disabledAdmin.AccountStatusText);

        var locked = new UserInfo { SamAccountName = "b", Enabled = true, AccountLocked = true };
        Assert.Equal("Verrouillé", locked.AccountStatusText);

        var admin = new UserInfo { SamAccountName = "c", Enabled = true, IsAdministrator = true };
        Assert.Equal("Administrateur", admin.AccountStatusText);

        var standard = new UserInfo { SamAccountName = "d", Enabled = true };
        Assert.Equal("Standard", standard.AccountStatusText);
    }

    [Fact]
    public void UserInfo_DisplayText_FallsBackToSamAccountName()
    {
        Assert.Equal("bob", new UserInfo { SamAccountName = "bob" }.DisplayText);
        Assert.Equal("Bob Martin", new UserInfo { SamAccountName = "bob", DisplayName = "Bob Martin" }.DisplayText);
    }

    [Fact]
    public void SyncResult_CarriesUsers()
    {
        var result = new SyncResult
        {
            Users = new[] { new UserInfo { SamAccountName = "bob", DistinguishedName = "CN=bob,DC=lab,DC=local" } },
        };

        Assert.Single(result.Users);
        Assert.Equal("bob", result.Users[0].SamAccountName);
    }

    [Fact]
    public void SyncRunSummary_CarriesUsersRead()
    {
        var summary = new SyncRunSummary { UsersRead = 28 };

        Assert.Equal(28, summary.UsersRead);
    }

    [Fact]
    public void ParseUsers_ClassifiesAdministratorsFromTheAdminDnSet()
    {
        var parser = new AdOutputParser();
        var json =
            """
            [{"SamAccountName":"bob","DistinguishedName":"CN=bob,OU=Users,DC=sarytany,DC=local","Enabled":true},
             {"SamAccountName":"admin","DistinguishedName":"CN=admin,OU=Users,DC=sarytany,DC=local","Enabled":true}]
            """;

        var admins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CN=admin,OU=Users,DC=sarytany,DC=local",
        };

        var users = parser.ParseUsers(json, admins);

        Assert.Equal(2, users.Count);
        Assert.False(users[0].IsAdministrator);
        Assert.True(users[1].IsAdministrator);
    }

    [Fact]
    public void ParseAdministratorDns_ReadsTheDnList()
    {
        var parser = new AdOutputParser();
        var json = "[\"CN=admin,OU=Users,DC=sarytany,DC=local\",\"CN=root,DC=sarytany,DC=local\"]";

        var admins = parser.ParseAdministratorDns(json);

        Assert.Equal(2, admins.Count);
        Assert.Contains("CN=admin,OU=Users,DC=sarytany,DC=local", admins);
    }

    [Fact]
    public void ParseAdministratorDns_HandlesSingleObjectAndScalarForms()
    {
        var parser = new AdOutputParser();

        // ConvertTo-Json sérialise un objet unique en objet, et une chaîne
        // unique en scalaire : les deux formes doivent être comprises.
        var fromObject = parser.ParseAdministratorDns("{\"DistinguishedName\":\"CN=admin,DC=sarytany,DC=local\"}");
        var fromScalar = parser.ParseAdministratorDns("\"CN=admin,DC=sarytany,DC=local\"");

        Assert.Single(fromObject);
        Assert.Single(fromScalar);
        Assert.Contains("CN=admin,DC=sarytany,DC=local", fromObject);
        Assert.Contains("CN=admin,DC=sarytany,DC=local", fromScalar);
    }

    [Fact]
    public void ParseAdministratorDns_ToleratesEmptyOutput()
    {
        var parser = new AdOutputParser();

        Assert.Empty(parser.ParseAdministratorDns(string.Empty));
        Assert.Empty(parser.ParseAdministratorDns("null"));
    }
}
