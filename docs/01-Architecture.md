# ADOps GPO Manager — Plan d'architecture (Étape 1)

## 1. Structure des dossiers et fichiers

```
ADOps.GpoManager/
├── ADOps.GpoManager.sln               (7 projets : Core, PowerShell, ActiveDirectory, Templates, Infrastructure, App, Tests)
├── Directory.Build.props                (propriétés MSBuild communes)
├── Directory.Packages.props           (gestion centralisée des versions NuGet)
├── build.bat                            (restauration + build Debug de la solution)
├── publish.bat                          (publication autonome win-x64 → publish/)
├── README.md
├── docs/
│   ├── 01-Architecture.md             (ce document)
│   ├── 02-GpoCatalog-Extensibility.md (catalogue de modèles : ajouter un type de GPO)
│   ├── 03-SecurityHardening.md        (référentiel de durcissement AD/GPO)
│   ├── templates/template.schema.json (schéma JSON des modèles, draft 2020-12)
│   └── preview.html                   (maquette d'interface interactive)
├── backend/                             (logique métier — aucun UI)
│   ├── ADOps.GpoManager.Core/         (net10.0, aucune dépendance)
│   │   ├── Models/
│   │   │   ├── GpoSummary.cs          (GpoSummary, GpoStatus)
│   │   │   ├── GpoLinkInfo.cs         (lien GPO : GpoId, Target, …)
│   │   │   ├── GpoDeploymentModels.cs (GpoImpactEntry, GpoDeploymentPlan,
│   │   │   │                           GpoLinkOperationResult, GpoDeploymentResult)
│   │   │   ├── DeploymentModels.cs
│   │   │   ├── DomainContext.cs       (contexte de connexion : FQDN ou IPv4)
│   │   │   ├── ConnectionCredentials.cs / CredentialMode.cs
│   │   │   ├── RemoteCredentialHandoff.cs (transfert du mot de passe via DPAPI)
│   │   │   ├── UserInfo.cs            (fiche utilisateur AD DS)
│   │   │   ├── OuInfo.cs              (unité d'organisation)
│   │   │   ├── ComputerInfo.cs / ComputerHealthInfo.cs
│   │   │   ├── NetworkHostInfo.cs     (hôte découvert sur le sous-réseau)
│   │   │   ├── AuditEntry.cs          (AuditEntry, AuditResult, AuditActions)
│   │   │   ├── AuditContext.cs
│   │   │   ├── HealthCheckReport.cs   (HealthCheckReport, HealthCheckStep, CheckStatus)
│   │   │   ├── OperationResult.cs     (OperationResult<T>, OperationError)
│   │   │   ├── PolicyDefinition.cs / PolicyTemplate.cs
│   │   │   ├── DepartmentMapping.cs
│   │   │   ├── SyncResult.cs / SyncRunSummary.cs
│   │   │   └── ExecutorMode.cs        (Local / Remote)
│   │   ├── Commands/
│   │   │   ├── PowerShellCommand.cs       (base : script → JSON, timeout)
│   │   │   ├── SimplePowerShellCommand.cs
│   │   │   ├── TestConnectionCommand.cs   (DNS / WinRM / Auth / Modules)
│   │   │   ├── TestAdAccessCommand.cs
│   │   │   ├── TestGroupPolicyCapabilityCommand.cs
│   │   │   ├── ListGposCommand.cs         (Get-GPO -All)
│   │   │   ├── ListGpoLinksCommand.cs
│   │   │   ├── GetGpoReportCommand.cs / GetGpoResultantSetCommand.cs
│   │   │   ├── ListOusCommand.cs          (Get-ADOrganizationalUnit)
│   │   │   ├── ListComputersCommand.cs    (Get-ADComputer)
│   │   │   ├── ListUsersCommand.cs        (Get-ADUser -Filter *)
│   │   │   ├── LinkGpoCommand.cs          (New-GPLink + Get-GPInheritance)
│   │   │   ├── UnlinkGpoCommand.cs        (Remove-GPLink)
│   │   │   └── DiscoverSubnetHostsCommand.cs (balayage ICMP /24, pool de runspaces)
│   │   ├── Validation/
│   │   │   ├── DomainValidator.cs         (FQDN, nom de domaine, IPv4)
│   │   │   ├── DistinguishedNameValidator.cs (DN d'OU + échappement littéral PowerShell)
│   │   │   └── UserValidator.cs
│   │   └── Services/
│   │       ├── ILogger.cs                 (ILogger, LogSeverity)
│   │       ├── IAuditService.cs
│   │       ├── IAuthenticationService.cs
│   │       ├── ISessionBootstrapper.cs    (session implicite : remplace l'écran de connexion)
│   │       ├── IHealthCheckService.cs
│   │       ├── IGpoService.cs / IGpoLifecycleService.cs / IGpoCatalogService.cs
│   │       ├── IGpoDeploymentService.cs   (PlanDeploymentAsync / DeployAsync / UnlinkGpoAsync)
│   │       ├── IDirectoryService.cs       (OU, utilisateurs, ordinateurs, liens GPO)
│   │       ├── INetworkDiscoveryService.cs
│   │       ├── IInventorySyncService.cs
│   │       └── IDepartmentMappingService.cs
│   │
│   ├── ADOps.GpoManager.PowerShell/   (net10.0, référence Core + ProtectedData)
│   │   ├── Host/
│   │   │   ├── IPowerShellExecutor.cs     (IPowerShellExecutor, IPowerShellExecutorFactory)
│   │   │   └── PowerShellExecutionResult.cs
│   │   ├── LocalExecutor.cs               (powershell.exe local)
│   │   ├── RemoteExecutor.cs              (Invoke-Command / WinRM)
│   │   ├── PowerShellExecutorFactory.cs
│   │   ├── PowerShellProcessRunner.cs     (lancement, UTF-8, timeout, kill)
│   │   ├── PowerShellOutputParser.cs      (JSON → modèles)
│   │   ├── ErrorInterpreter.cs            (stderr → cause + résolution)
│   │   └── Services/
│   │       ├── HealthCheckService.cs
│   │       ├── GpoService.cs
│   │       └── GpoResultService.cs
│   │
│   ├── ADOps.GpoManager.ActiveDirectory/ (net10.0, référence Core + PowerShell)
│   │   ├── AdOutputParser.cs               (sorties AD DS → modèles)
│   │   └── Services/
│   │       ├── DirectoryService.cs        (implémente IDirectoryService)
│   │       ├── GpoDeploymentService.cs    (implémente IGpoDeploymentService : impact + liens)
│   │       ├── NetworkDiscoveryService.cs (implémente INetworkDiscoveryService)
│   │       ├── ComputerAvailabilityService.cs
│   │       └── ActiveDirectoryInventoryService.cs
│   │
│   ├── ADOps.GpoManager.Templates/     (net10.0, référence Core, JSON embarqués)
│   │   ├── TemplateCatalog.cs           (catalogue : schéma versionné, validation générique)
│   │   └── Templates/
│   │       └── *.json                   (16 modèles de durcissement/restriction,
│   │                                     clé : templateId — voir docs/02)
│   │
│   └── ADOps.GpoManager.Infrastructure/ (net10.0-windows, référence Core + ActiveDirectory + Microsoft.Data.Sqlite)
│       ├── Logging/
│       │   └── FileLogger.cs              (rotation par taille)
│       ├── Storage/
│       │   ├── StoragePaths.cs            (%LOCALAPPDATA%\ADOps\GpoManager)
│       │   ├── DatabaseSchema.cs / SqliteDatabase.cs (schéma + fabrique de connexions)
│       │   ├── AuditRepository.cs
│       │   ├── AuditService.cs            (implémente IAuditService)
│       │   ├── ComputerHealthRepository.cs
│       │   ├── InventoryRepository.cs / InventoryQueryRepository.cs
│       │   ├── InventorySyncService.cs
│       │   └── DomainProfileRepository.cs / DepartmentMappingService.cs
│       └── Configuration/
│           ├── AppConfiguration.cs
│           ├── IConfigurationStore.cs
│           └── JsonConfigurationStore.cs  (settings.json)
│       └── Services/
│           ├── SessionBootstrapper.cs     (implémente ISessionBootstrapper : session implicite)
│           └── WindowsSessionProbe.cs     (identité Windows : compte, élévation, domaine)
│
├── frontend/                            (interface utilisateur)
│   └── ADOps.GpoManager.App/          (net10.0-windows + WPF, référence les 5 projets backend)
│       ├── App.xaml / App.xaml.cs         (composition, DataTemplates, démarrage sans connexion)
│       ├── Bootstrapper.cs                (conteneur DI)
│       ├── Services/
│       │   ├── INavigationService.cs / NavigationService.cs
│       │   ├── ILauncherService.cs / LauncherService.cs
│       │   ├── AdminToolsService.cs        (outils d'administration Windows : ADUC, MMC, dossier de démarrage)
│       │   └── ConnectionState.cs         (état partagé, ObservableObject)
│       ├── Controls/Converters/Converters.cs
│       ├── ViewModels/                    (MVVM, CommunityToolkit.Mvvm)
│       │   ├── ViewModelBase.cs
│       │   ├── MainViewModel.cs            (session implicite + synchronisation globale)
│       │   ├── DashboardViewModel.cs      (tableau de bord + découverte du sous-réseau)
│       │   ├── ClientAccountsViewModel.cs (comptes clients + outils d'administration)
│       │   ├── DirectoryViewModel.cs      (arborescence OU / utilisateurs / liens GPO)
│       │   ├── DeploymentsViewModel.cs    (sélection multiple GPO + rapport d'impact)
│       │   ├── GpoListViewModel.cs
│       │   ├── ComputerDetailViewModel.cs
│       │   ├── ParcViewModel.cs / DepartmentMappingViewModel.cs
│       │   ├── CatalogViewModel.cs / BackupsViewModel.cs
│       │   ├── AuditViewModel.cs / SettingsViewModel.cs
│       │   └── HealthCheckStepViewModel.cs
│       ├── Views/                         (code-behind = DataContext uniquement)
│       │   ├── MainWindow.xaml
│       │   ├── DashboardView.xaml / DirectoryView.xaml / DeploymentsView.xaml
│       │   ├── GpoListView.xaml / ComputerDetailView.xaml
│       │   ├── ParcView.xaml / DepartmentMappingView.xaml
│       │   ├── ClientAccountsView.xaml / SessionLockWindow.xaml
│       │   ├── CatalogView.xaml / BackupsView.xaml
│       │   ├── AuditView.xaml / SettingsView.xaml
│       │   └── (aucun écran de connexion : la session Windows est l'identité)
│       └── Resources/Styles.xaml          (thème sombre Fluent)
│
└── tests/
    └── ADOps.GpoManager.Tests/          (xunit)
        ├── PowerShellOutputParserTests.cs
        ├── DomainValidatorTests.cs
        ├── GpoSummarySerializationTests.cs
        ├── ListGposCommandTests.cs
        ├── ErrorInterpreterTests.cs
        ├── AuditServiceTests.cs
        └── TemplateCatalogTests.cs      (invariants du catalogue de modèles)
```

### Packages NuGet (versions exactes, figées dans Directory.Packages.props)

| Package | Version | Usage |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | ObservableObject, [ObservableProperty], [RelayCommand] |
| Microsoft.Data.Sqlite | 10.0.12 | base d'audit locale |
| Microsoft.Extensions.DependencyInjection | 10.0.12 | conteneur DI |
| Microsoft.NET.Test.Sdk | 17.12.0 | tests |
| System.Security.Cryptography.ProtectedData | 10.0.12 | transfert DPAPI du mot de passe (remote) |
| xunit | 2.9.3 | tests |
| xunit.runner.visualstudio | 2.8.2 | découverte des tests |

Aucune dépendance externe n'est ajoutée : l'accès AD DS et les GPO passent
par les modules PowerShell **GroupPolicy** et **ActiveDirectory** (RSAT),
exécutés via `powershell.exe` (local) ou `Invoke-Command` (WinRM distant).

## Phase 2 — Annuaire, découverte réseau, déploiement de GPO

### Commandes typées ajoutées (Core/Commands)

| Commande | Script | Sortie |
|---|---|---|
| `ListUsersCommand` | `Get-ADUser -Filter *` (+ propriétés projetées) | `UserInfo[]` JSON |
| `LinkGpoCommand` | `New-GPLink -Guid … -Target …` puis `Get-GPInheritance` (vérification) | liens relus |
| `UnlinkGpoCommand` | `Remove-GPLink -Guid … -Target …` puis relecture | liens relus |
| `DiscoverSubnetHostsCommand` | pool de runspaces (64) : `Ping.Send` /24 + `Dns.GetHostEntry` inverse | `{IpAddress, HostName, Online}[]` |

### Contrats ajoutés (Core/Services)

```csharp
public interface IDirectoryService          // ListUsersAsync : utilisateurs du domaine
public interface INetworkDiscoveryService   // DiscoverAsync : balayage /24 + croisement AD DS
public interface IGpoDeploymentService      // PlanDeploymentAsync (validation + impact),
                                            // DeployAsync (New-GPLink), UnlinkGpoAsync
```

### Rapport d'impact (calcul)

L'impact d'une GPO sur une OU cible est calculé à partir des objets
réellement situés dans le **sous-arbre** de l'OU (le `DistinguishedName`
de l'utilisateur/ordinateur se termine par le DN de l'OU). Le calcul est
local (données AD DS déjà chargées), donc le rapport est mis à jour
**en temps réel** à chaque changement de sélection (case à cocher).
Les liens existants (cache local de synchronisation) signalent les doublons
(`New-GPLink` est idempotent).

### Découverte du masque réseau

1. L'adresse IPv4 du contrôleur est obtenue (littérale si la connexion
   est par IP, sinon résolution DNS locale du FQDN).
2. Le préfixe /24 (3 premiers octets) est déduit.
3. Balayage : 254 adresses, ICMP en parallèle bornée (64) — en C# local
   (mode RSAT) ou sur le DC via la commande typée (mode WinRM).
4. Résolution DNS inverse de chaque hôte en ligne.
5. Croisement avec l'inventaire AD DS : correspondance par nom
   (étiquette du DNS inverse), puis par adresse IP (résolution directe
   du nom de l'ordinateur). Les objets AD DS non joignables sont
   conservés comme « hors ligne » : **ICMP n'est jamais utilisé comme
   preuve d'extinction** (il est souvent bloqué par les pare-feu).

### Sécurité du déploiement

- Aucune modification sans **plan validé** (étape de validation explicite).
- Cibles (`DistinguishedName`) validées par `DistinguishedNameValidator`
  (composants `CN=`/`OU=`/`DC=` uniquement) puis échappées
  (guillemets simples doublés) avant injection dans le script.
- Confirmation utilisateur explicite avant `New-GPLink`.
- Chaque lien appliqué/supprimé est audité (SQLite) avec cibles et résultat.

## 2. Interfaces principales

```csharp
// PowerShell
public interface IPowerShellExecutor
{
    ExecutorMode Mode { get; }
    Task<PowerShellExecutionResult> ExecuteAsync(PowerShellCommand command, CancellationToken ct = default);
}
public interface IPowerShellExecutorFactory
{
    IPowerShellExecutor Local { get; }
    IPowerShellExecutor CreateRemote(string targetFqdn);
}

// Services métier
public interface IHealthCheckService
{
    Task<HealthCheckReport> RunAsync(string serverFqdn, string domainFqdn, CancellationToken ct = default);
}
public interface IGpoService
{
    Task<OperationResult<IReadOnlyList<GpoSummary>>> ListGposAsync(DomainContext context, CancellationToken ct = default);
}
public interface IAuditService
{
    Task LogAsync(string action, string target, AuditResult result, string message, string? details = null);
    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int maxEntries = 200);
    Task<int> GetCountAsync();
}
public interface ILogger { /* Trace/Debug/Information/Warning/Error/Critical */ }
```

## 3. Flux de données

```
UI (XAML)
  → ViewModel (CommunityToolkit.Mvvm, aucune logique métier dans le code-behind)
    → Service (HealthCheckService / GpoService)
      → Commande typée (TestConnectionCommand / ListGposCommand : script validé + JSON)
        → PowerShellExecutor (Local ou Remote via WinRM)
          → powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand <Base64 UTF-16>
          ← stdout (JSON UTF-8) / stderr / code de sortie
      ← PowerShellOutputParser (JSON → GpoSummary[]) — liste vide = résultat valide
      ← ErrorInterpreter (stderr → titre + cause probable + résolution)
      → IAuditService (SQLite : timestamp, utilisateur Windows, action, résultat)
    ← OperationResult<T> ou HealthCheckReport
  ← mise à jour des propriétés liées (INotifyPropertyChanged)
  → journal fichier (logs/adops.log, rotation 5 Mo × 5 fichiers)
```

Détails du Health Check (exécution séquentielle, chaque étape chronométrée) :

1. **Résolution DNS** — `Resolve-DnsName -Name <fqdn> -Type A` (local, 20 s)
2. **Service WinRM** — `Test-WSMan -ComputerName <fqdn>` (distant, 60 s)
3. **Authentification** — `Invoke-Command -ComputerName <fqdn> { $env:USERNAME }` (60 s, ignorée si WinRM échoue)
4. **Modules PowerShell** — `Get-Module -ListAvailable -Name GroupPolicy,ActiveDirectory` (local, 30 s)

Synthèse : le domaine est « sain » si DNS répond ET (RSAT local présent OU authentification distante OK).
Le mode recommandé est `Local` si RSAT est présent, sinon `Remote`.

## 4. Modèles de données principaux

- **GpoSummary** — Id (Guid), DisplayName, Description, CreationTime, ModificationTime,
  Status (GpoStatus), DomainName, StatusText (libellé).
- **DomainContext** — ServerFqdn, DomainFqdn, DomainNetbios, ExecutorMode, UserName,
  ConnectedAtUtc, LastHealthCheck. Le constructeur valide et normalise les noms.
- **AuditEntry** — Id, TimestampUtc, UserName (WindowsIdentity courante), Action (constante
  AuditActions), Target, Result (Information/Success/Warning/Failure), Message, Details.
- **HealthCheckReport / HealthCheckStep** — étapes avec statut (Passed/Warning/Failed/Skipped),
  message, durée, remediation ; rapport avec IsHealthy, RecommendedExecutorMode, Summary.
- **OperationResult&lt;T&gt; / OperationError** — résultat structuré : Title, Cause, Remediation,
  RawError (jamais un simple « exit code 1 »).

## 5. Décisions de sécurité

- Aucune saisie libre de commande PowerShell : seules les commandes typées existent.
- Aucun credential stocké : identité Windows courante (double-hop Kerberos non géré — limitation Phase 1).
- Scripts passés en `-EncodedCommand` (Base64 UTF-16) : aucun problème d'injection par les noms
  (FQDN validé par DomainValidator avant injection).
- Chaque opération (Health Check, liste GPO, changement de configuration) est auditée.

## Phase 3 — Diagnostic & Réparation (moteur intelligent)

### Nouveaux modules

| Module | Rôle |
|---|---|
| `backend/ADOps.GpoManager.Core/Diagnostics/DiagnosticModels.cs` | Modèles standardisés : `DiagnosticCheckResult` (Name, Status, ErrorCode, Details, SuggestedFixes, RequiresElevation, CanAutoFix), `RemediationAction`, `RemediationPlan`, `DiagnosticReport` (avec `RootCauses`). |
| `backend/ADOps.GpoManager.Core/Diagnostics/IDiagnosticEngine.cs` | Contrat du moteur : `RunAllAsync`, `RunCheckAsync` (post-vérification), registre ordonné des contrôles avec dépendances. |
| `backend/ADOps.GpoManager.Core/Services/IRemediationService.cs` | Contrat de l'assistant de réparation : `BuildPlan` (aperçu), `ExecuteAsync` (confirmation + dry-run), `RetestAsync`. |
| `backend/ADOps.GpoManager.Core/Commands/Diagnose*.cs` | 5 commandes typées (JSON strict) : DNS (ICMP, résolution domaine, SRV `_ldap._tcp.dc._msdcs`, DNS client), WinRM (ports 5985/5986, Test-WSMan, service, pare-feu), RSAT (modules, OS, élévation), Authentification (identité distante, classification réseau/identification/droits), GPO (Get-GPO -All, SYSVOL). |
| `backend/ADOps.GpoManager.Core/Commands/{EnableWinRm,InstallRsat,ClearDnsCache}Command.cs` | Commandes de remédiation (élévation, exécution locale). |
| `backend/ADOps.GpoManager.ActiveDirectory/Services/DiagnosticEngine.cs` | Orchestration en séquence logique, fail-safe (WinRM sondé localement, jamais via WinRM ; contrôles dépendants marqués « non exécutés »), isolation des causes racines, audit de chaque contrôle. |
| `backend/ADOps.GpoManager.ActiveDirectory/Services/RemediationService.cs` | Plan prévisualisable, application uniquement après confirmation explicite, mode Dry-Run (zéro modification), journalisation sans secret, post-vérification par rejeu du contrôle cible. |
| `frontend/.../ViewModels/DiagnosticsViewModel.cs` + `Views/DiagnosticsView.xaml` | En-tête (contrôleur cible, IP résolue, domaine détecté), grille d'état des 6 services, barre d'actions (Diagnostiquer tout / Réparer / Re-tester / mode diagnostic), détail par contrôle, plan de remédiation avec cases et badges de risque, résultats avec post-vérification. |
| `frontend/.../Controls/Converters/DiagnosticConverters.cs` | Convertisseurs statut → couleur/texte, risque → libellé. |

### Règles de sécurité du moteur

1. **Diagnostic seul** : le moteur ne modifie jamais le système.
2. **Confirmation explicite** avant toute application ; l'aperçu liste chaque action, son risque et son besoin d'élévation.
3. **Dry-Run par défaut** dans l'interface : rien n'est modifié tant que l'utilisateur ne décoche pas le mode diagnostic.
4. **Fail-safe** : un service hors ligne n'est jamais réparé à travers lui-même (WinRM se répare en local). Aucun repli non sécurisé (TrustedHosts) n'est activé automatiquement.
5. **Journalisation** (`logs/adops.log`) : horodatage, commande, durée, résultat, code d'erreur — **jamais** de mot de passe ni de credential en clair (transit DPAPI par stdin inchangé).
6. **Audit SQLite** de chaque contrôle (`DiagnosticRun`) et de chaque correction (`RemediationPlan`, `RemediationApply`).