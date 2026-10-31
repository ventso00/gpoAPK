# 11 — Cahier des charges « VM / hors-ligne » : diagnostic et architecture cible

Date : 22 octobre 2026. Périmètre : sources du projet `gpo-main` (lecture seule, aucune modification),
+ contexte d'exécution fourni (application testée sur une **VM** hébergée sur le serveur réel,
**session administrateur** ouverte).

---

## 0. État d'avancement de la mise en œuvre (22/10/2026)

| Exigence | Statut | Implémentation |
|----------|--------|----------------|
| E1 Suppression de la connexion | **Livré** | `App.xaml.cs` ouvre directement `MainWindow` ; `LoginWindow.xaml(.cs)`, `LoginViewModel.cs`, `ILoginWindowService.cs`, `LoginWindowService.cs`, `PasswordBoxAssistant.cs` supprimés ; verrouillage de session repris par `SessionLockWindow` ; *Paramètres ▸ Rejouer la détection de session* |
| E2 Bouton « Synchroniser » | **Livré** | Bouton global `⟳ Synchroniser` en barre supérieure (`MainViewModel.SyncNowCommand`), en plus du bouton par page du parc |
| E4 Synchronisation complète | **Livré** | `ActiveDirectoryInventoryService` collecte désormais les **comptes utilisateurs** ; persistance en table `client_accounts` ; migration de schéma `users_read` |
| E3 Vue « comptes clients » | **Livré** | `ClientAccountsViewModel` + `ClientAccountsView.xaml` (filtres, badges admin/standard, verrouillé) ; `AdminToolsService` (ADUC, dsr, gpedit, compmgmt, services, lusrmgr, disksnap) + dossier « Outils d'administration » localisé |
| E5 Mode hors-ligne | **Partiel** | Cache complet (comptes inclus) → consultation possible sans annuaire ; bandeau d'état de session ; **écritures encore à verrouiller hors-ligne** (lot P4) |
| E6 Application aux machines | **À faire** | Cible = OU uniquement ; `gpupdate /force` et relecture `gpresult /r` à ajouter (lot P4) |

### Où vivent les nouveaux composants

| Composant | Projet | TFM | Pourquoi |
|---|---|---|---|
| `MachineDomain` | `Core` | `net10.0` | P/Invoke Netlogon uniquement, agnostique OS |
| `SessionIdentity` (modèle) | `Core` | `net10.0` | Modèle pur, aucune API Windows |
| `ISessionBootstrapper` | `Core` | `net10.0` | Contrat |
| `WindowsSessionProbe` | `Infrastructure` | `net10.0-windows` | **Seul** projet autorisé à utiliser `WindowsIdentity` / `WindowsPrincipal` |
| `SessionBootstrapper` | `Infrastructure` | `net10.0-windows` | Implémentation : Netlogon + health check + contexte |
| `AdminToolsService` | `App` | `net10.0-windows` | `Process.Start`, chemins MMC, dossier localisé |
| `ClientAccountsViewModel` + vue | `App` | `net10.0-windows` | Présentation |

**Non vérifié** : aucune compilation ni exécution de tests n'a pu être réalisée (SDK .NET 10 absent
de la machine d'analyse). Le build et `dotnet test` **doivent** être exécutés sur la machine
disposant du SDK avant toute publication.

> **Complément (22/10/2026)** : la fermeture de l'application après la synchronisation a été
> diagnostiquée et corrigée — voir [`12-Diagnostic-Quitte-Apres-Synchronisation.md`](12-Diagnostic-Quitte-Apres-Synchronisation.md).
> Corrections portant : respect de la cible configurée (`settings.json`), requête de
> classification administrateur sans rétro-liaison `memberOf`, suspension du verrouillage de
> session pendant la collecte, filets d'exception sur les threads secondaires, tableau de bord
> « session et synchronisation ».

---

## 1. Ce que le cahier des charges demande, exactement

| # | Exigence | Formulation opérationnelle |
|---|----------|---------------------------|
| E1 | Suppression de la connexion | Retirer **complètement** l'écran/page de connexion de l'exécutable |
| E2 | Bouton « Synchroniser » | Action manuelle, globale, collecte automatique des données GPO |
| E3 | Vue « comptes clients » | Écran dédié listant les comptes clients, adossé aux outils d'administration Windows (`C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Administrative Tools`) |
| E4 | Synchronisation | Le bouton collecte automatiquement tout ce qui est nécessaire à la gestion des GPO |
| E5 | Mode hors-ligne | Application **entièrement autonome**, qui fonctionne sans dépendance externe |
| E6 | Sélection + application | Choix des GPO disponibles, puis application aux machines clientes |

---

## 2. État des lieux : ce que le code fait réellement aujourd'hui

### 2.1 Architecture existante (conforme à `docs/01-Architecture.md`)

```
ADOps.GpoManager.sln (7 projets)
├── Core            (net10.0)      modèles, commandes PowerShell typées, validation, contrats, sécurité
├── PowerShell      (net10.0)      exécuteurs local / WinRM, parsing, interpréteur d'erreurs FR/EN
├── ActiveDirectory (net10.0)      inventaire AD, annuaire, découverte réseau, déploiement GPO, diagnostic
├── Infrastructure  (net10.0-win) SQLite (audit + cache), journalisation, configuration JSON
├── Templates       (net10.0)      16 modèles JSON de stratégies embarqués + gpo_catalog.json
├── App             (net10.0-win) WPF / MVVM (CommunityToolkit.Mvvm 8.4.2) + DI (MS.Extensions.DI 10.0.12)
└── Tests           (net10.0)      xUnit — 11 fichiers, ~94 [Fact] + [Theory]
```

Le découpage est sain et mérite d'être conservé tel quel. **Aucun** de ces 7 projets ne doit être
scindé ni fusionné pour l'analyse du besoin.

### 2.2 Cartographie des exigences

| # | Exigence | Implémentation actuelle | Verdict |
|---|----------|------------------------|---------|
| E1 | Suppression connexion | `App.xaml.cs:42-45` ouvre `LoginWindow` au démarrage ; `MainWindow.xaml.cs:74-115` la rouvre au verrouillage de session (et **ferme l'app** si l'utilisateur annule) ; `SettingsViewModel.cs:215` (« Changer de connexion ») ; `Bootstrapper.cs:104,125` enregistrent `ILoginWindowService` + `LoginWindow` transitoire. `LoginWindow.xaml` = 404 lignes, `WindowState="Maximized"`. | **À supprimer (3 points de câblage + 2 fichiers)** |
| E2 | Bouton « Synchroniser » | **Existe déjà**, mais **localisé dans `ParcView.xaml:29`** → `ParcViewModel.SynchronizeAsync` (`ParcViewModel.cs:211`). Aucun bouton global. `MainViewModel` ne déclenche qu'une **auto-sync** (désactivée par défaut : `AppConfiguration.AutoSyncEnabled = false`). | **À remonter en global** |
| E3 | Vue « comptes clients » | **Inexistante comme vue dédiée.** `DirectoryView` liste les utilisateurs rattachés aux OU, mais en **lecture directe** (`DirectoryService.ListUsersAsync` → `Get-ADUser` à chaque affichage). Aucune intégration des outils d'administration (`dsa.msc`, etc.) : 0 occurrence de `ProgramData` / `Administrative Tools` / `mmc` dans le code. | **À créer** |
| E4 | Synchronisation complète | `ActiveDirectoryInventoryService.ReadInventoryAsync` (l. 32-57) lit **4 objets** : OU, ordinateurs, GPO, liens. **Les utilisateurs ne sont PAS collectés.** | **Incomplet (~50 %)** |
| E5 | Mode hors-ligne | Aucune dépendance Internet (OK). **Mais l'affichage n'est pas hors-ligne** : `users` absents du cache SQLite, GPO relues en direct par `GpoService`. | **Cache incomplet** |
| E6 | Sélection + application | **Bien avancé.** `IGpoDeploymentService` (plan → `New-GPLink` → audit) + `AutomateGpoViewModel` (assistant 3 étapes) + `DeploymentsViewModel`. **Mais la cible est l'OU**, pas la machine ; pas de `gpupdate` ni de relecture `gpresult` post-application ; pas de filtrage de sécurité par groupe. | **À étendre** |

### 2.3 Ce qui ne va pas dans le « contexte de test actuel »

1. **Le SDK .NET 10 n'est pas installé sur la machine d'analyse** (`dotnet` absent du `PATH`).
   Aucune compilation ni exécution de tests n'est donc possible ici. Le build devra être fait sur la VM.
2. **Le mode « démo » décrit dans `docs/07-Audit-et-Recette-Pre-HyperV.md` (l. 7, 25-31) n'existe pas dans le code.**
   Vérification : 0 occurrence de `DemoOnly2026` / `demo.local` / `DemoMode` dans les `.cs`.
   `docs/06-Mode-Demo-Local.md`, référencé deux fois, **est absent du dépôt**.
   Le mode démo n'existe que comme **prototype HTML** (`docs/preview.html`, `publish/demo/preview.html`).
   → *La documentation décrit un état du code qui n'a jamais atterri (ou qui a été perdu). À réaligner.*
3. **`docs/09-Sync-et-Application-Reelle.md` § 6** renvoie l'utilisateur vers ce mode démo inexistant.
4. Le protocole de test réel est bien documenté (`docs/07` § « Recette », `docs/09`, `docs/10`),
   mais il suppose **l'écran de connexion**, qui doit disparaître.
5. `Lancer-Production.cmd` affiche en dur « *Connexion domaine Sarytany* » — à corriger.

---

## 3. Points d'attention majeurs (à trancher avant de coder)

### 3.1 Supprimer la connexion ≠ supprimer la sécurité

Today, l'écran de connexion est la barrière qui empêche un poste non autorisé d'agir sur le domaine.
Une fois l'écran retiré, **l'identité utilisée est implicitement celle de la session Windows** :

- c'est exactement le mode **Local (RSAT)** déjà supporté (`LocalExecutor` → `powershell.exe` local,
  `docs/05-Lab-VMware.md` § 8 : « *mode local : les commandes utilisent le compte Windows de la session* ») ;
- c'est cohérent avec le contexte fourni (« session administrateur »).

Conséquence : il faut remplacer la barrière par un **contrôle de préconditions au démarrage**, sinon
l'application devient un lanceur `New-GPLink` sans contrôle pour quiconque ouvre le binaire.

### 3.2 Le DC / le domaine doivent être auto-détectés

Le DC n'est plus saisi. Il faut le découvrir depuis la session :

| Information | Source déjà disponible | Remarque |
|---|---|---|
| Domaine de la session | `System.DirectoryServices.ActiveDirectory.Domain.GetComputerDomain()` | `System.DirectoryServices` est **inclus dans le framework .NET** → aucune dépendance NuGet |
| Contrôleur de domaine | `DcLocator.Discover(domainFqdn)` (`Core/Network/DcLocator.cs`, API Netlogon `DsGetDcName`) | **Déjà implémenté et testé** (`DcLocatorTests.cs`) |
| Identité courante | `WindowsIdentity.GetCurrent()` | Déjà utilisé dans `MainViewModel.CurrentUser` |
| Élévation | `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)` | Pas encore utilisé (les cmdlets `Diagnose*Command` le font en PowerShell) |

→ `DcLocator` est le bon point d'appui : il suffit de l'invoquer avec le domaine auto-détecté.

### 3.3 « Hors-ligne » a deux sens — il faut choisir

| Sens | Définition | État actuel | Charge |
|---|---|---|---|
| (a) **Sans Internet** | Aucun service externe, aucun appel réseau sortant, runtime embarqué | **Déjà satisfait** (single-file, zéro HTTP) | 0 |
| (b) **Sans annuaire joignable** | L'app s'ouvre et affiche le dernier état connu quand le DC est injoignable | **Non satisfait** | Moyenne (cache complet + mode dégradé) |

Le cahier des charges (« *entièrement autonome et hors-ligne* ») combined au contexte VM
suggestione (b). **Recommandation : traiter (b)**, (a) étant déjà acquis.

### 3.4 Appliquer une GPO « aux machines clientes » : le modèle technique

Le modèle GPO n'applique **pas** une stratégie à un objet : elle s'applique à un **conteneur**
(OU) ou via un **filtrage de sécurité** (groupe). L'exigence E6 doit donc être traduite ainsi :

```
GPO × (machines sélectionnées)
   → regrouper les machines par OU parente        (déduplication)
   → New-GPLink par couple (GPO, OU)               (idem, idempotent)
   → [nouveau] filtrage de sécurité par groupe    (exclure les comptes admin)
   → [nouveau] gpupdate /force sur chaque poste     (WinRM distant obligatoire)
   → [nouveau] relecture gpresult /r + vérification du regkey
```

Point bloquant à connaître : **`gpupdate` et `gpresult` s'exécutent sur le poste cible**, donc
**WinRM doit être actif sur les clients** (et pas seulement sur le DC) — `docs/05-Lab-VMware.md` § 8
le signale déjà : « *WinRM est requis pour le RSoP, même en mode local* ».

---

## 4. Architecture cible

### 4.1 Nouveaux composants backend

```
ADOps.GpoManager.Core/                          (contrats + modèles + commandes)
├── Network/SessionIdentity.cs                 (NOUVEAU) identité de session : domaine, DC, user, élevé
├── Services/ISessionBootstrapper.cs           (NOUVEAU) contrat de l'auto-connexion
├── Services/IAdminToolsService.cs             (NOUVEAU) contrat d'intégration MMC / outils admin
├── Services/IClientAccountsService.cs          (NOUVEAU) lecture + cache des comptes clients
├── Services/ISyncOrchestrator.cs              (NOUVEAU) synchronisation par étapes (remplace le monolithe)
├── Models/SyncStepResult.cs / SyncProgress.cs (NOUVEAU) progression par étape (comme HealthCheckStep)
├── Models/ClientAccount.cs                    (NOUVEAU) modèle de compte client + classification
├── Models/AdminToolDescriptor.cs              (NOUVEAU) un outil d'administration
└── Commands/
    ├── ListClientAccountsCommand.cs           (NOUVEAU) Get-ADUser enrichi (groupes, admin, dernier logon)
    ├── InvokeGpUpdateCommand.cs               (NOUVEAU) gpupdate /force sur le poste cible
    ├── GetGpResultPerComputerCommand.cs        (NOUVEAU) gpresult /r sur le poste cible
    ├── SetGpoSecurityFilterCommand.cs         (NOUVEAU) Add-GPRecursionPermission (groupe cible)
    └── GetDomainInfoCommand.cs                (NOUVEAU) Get-ADDomain auto-détection

ADOps.GpoManager.ActiveDirectory/
└── Services/
    ├── SessionBootstrapper.cs                 (NOUVEAU) implémente ISessionBootstrapper
    ├── SyncOrchestrator.cs                    (NOUVEAU) orchestrateur 7 étapes
    └── ClientAccountService.cs                (NOUVEAU) implémente IClientAccountsService

ADOps.GpoManager.Infrastructure/
├── Storage/DatabaseSchema.cs                  (MODIF) + table `client_accounts`, `sync_steps`
├── Storage/InventoryRepository.cs             (MODIF) persistance des comptes clients
├── Storage/InventoryQueryRepository.cs        (MODIF) lecture des comptes clients depuis le cache
└── Configuration/AppConfiguration.cs          (MODIF) + AutoConnect, OfflineMode, SyncScope

ADOps.GpoManager.Windows/                      (NOUVEAU — projet net10.0-windows)
└── AdminTools/AdminToolsService.cs            intégration C:\ProgramData\...\Administrative Tools
```

> **Recommandation de découpage** : l'intégration des outils d'administration touche au registre
> Windows, aux raccourcis `.lnk` et à `Process.Start`. `Core` et `PowerShell` doivent rester
> `net10.0` (agnostiques OS) — c'est une règle structurante du projet aujourd'hui.
> Deux options, par ordre de préférence :
> 1. **Créer un projet `ADOps.GpoManager.Windows`** (net10.0-windows) dédié → frontière propre, testable.
> 2. **Placer `AdminToolsService` dans `Infrastructure`** (déjà `net10.0-windows`) → plus rapide,
>    pas de nouveau projet, mais `Infrastructure` devient le point de mélange (stockage + OS).

### 4.2 Nouveau flux de démarrage (sans écran de connexion)

```
App.OnStartup
   └─► Bootstrapper.ConfigureServices()              (inchangé)
   └─► VerifyBinaryIntegrity()                        (inchangé)
   └─► SessionBootstrapper.BootstrapAsync()           (NOUVEAU)
         1. WindowsIdentity.GetCurrent()  → user + elevated ?
         2. Domain.GetComputerDomain()   → domaine de la session
         3. DcLocator.Discover(domain)    → FQDN du DC
         4. HealthCheck (local, 6 étapes) → RSAT présent ? AD accessible ?
         5. Construire un DomainContext(ExecutorMode.Local, credentials: current account)
         6. ConnectionState.SetConnection(context)
         7. Audit "SessionAutonyme" + bandeau d'état
         └─► SI échec → banner "Mode dégradé" + ouverture en lecture seule (cache)
   └─► MainWindow.Show()                              (au lieu de LoginWindow.Show())
```

Le `DomainContext` accepte déjà les identifiants vides (`DomainContext.cs:43-44` → `Environment.UserName`),
et `ConnectionCredentials` sans paramètre = compte courant. **Aucun changement de modèle n'est requis
pour passer en session implicite.**

### 4.3 SessionGuard : remplacer la « ré-authentification » par un « déverrouillage local »

`MainWindow.xaml.cs:74-115` ouvre le login au verrouillage et **shutdown** si l'utilisateur annule.
Sans écran de connexion, ce comportement doit devenir :

| Situation | Ancien comportement | Nouveau comportement |
|---|---|---|
| Inactivité > `SessionTimeoutMinutes` | Masque + re-login + shutdown si annulé | **Masque la fenêtre + ré-affiche un voile local « Session verrouillée — cliquez pour déverrouiller »** |
| Refus de déverrouiller | — | L'application **se ferme** (état verrouillé) → même niveau de garantie qu'avant |
| Identité | ressaisie du mot de passe | **inchangeée** (session Windows), mais re-vérifiée (domaine + DC toujours joignables ?) |

Le `SessionGuard` (`Core/Security/SessionGuard.cs`) est déjà testé (`SecurityTests.cs:86`) et **conserve
son API** : seul `MainWindow.xaml.cs` change. Le paramètre `security.sessionTimeoutMinutes` de
`settings.json` reste valable.

### 4.4 Le synchroniseur à étapes (E2 + E4)

Le prototype `docs/preview.html` définit déjà les 7 étapes (l. 1018-1026). On les aligne sur le backend :

| # | Étape | Commande / source | Persisté | Tolérance d'échec |
|---|---|---|---|---|
| 1 | Résolution DNS | `Resolve-DnsName` | – | bloquant |
| 2 | AD DS — annuaire | `Get-ADDomain` (NOUVEAU `GetDomainInfoCommand`) | – | bloquant |
| 3 | Unités d'organisation | `ListOusCommand` | `organizational_units` | partiel |
| 4 | Machines | `ListComputersCommand` + `DiscoverSubnetHostsCommand` | `computers` | partiel |
| 5 | **Comptes clients** | **NOUVEAU `ListClientAccountsCommand`** | **NOUVEAU `client_accounts`** | partiel |
| 6 | GPO du domaine | `ListGposCommand` + `ListGpoLinksCommand` | `gpos`, `gpo_links` | partiel |
| 7 | Catalogue de stratégies | `TemplateCatalog` + `GpoScriptCatalog` (JSON embarqué) | – | non bloquant |

Chaque étape renvoie un `SyncStepResult` (nom, statut, durée, message, détail) → l'UI affiche une
liste d'étapes vivantes, exactement comme le fait déjà `LoginViewModel.HealthCheckStepNames` /
`HealthCheckStepViewModel` (le composant existe, il suffit de le réutiliser).

**Le `SyncOrchestrator` remplace `ActiveDirectoryInventoryService.ReadInventoryAsync`** et renvoie
`SyncResult` enrichi (users inclus). `IInventorySyncService.SyncAsync` garde sa signature →
**aucune casse des ViewModels existants**.

### 4.5 Modèle de données — le trou à boucher

`DatabaseSchema.cs` ne contient **aucune table `users`**. Sans elle, la vue « comptes clients » est
impossible hors-ligne. Nouvelle table (schéma versionné, cf. § 5, lot P1) :

```sql
CREATE TABLE IF NOT EXISTS client_accounts (
    distinguished_name      TEXT PRIMARY KEY,
    sam_account_name        TEXT NOT NULL,
    display_name            TEXT,
    given_name              TEXT,
    surname                 TEXT,
    email                   TEXT,
    title                   TEXT,
    department              TEXT,
    ou_dn                   TEXT,
    enabled                 INTEGER NOT NULL DEFAULT 1,
    is_admin                INTEGER NOT NULL DEFAULT 0,   -- classification admin/standard
    admin_reason            TEXT,                        -- ex. 'memberof:CN=Domain Admins,...'
    primary_group_dn        TEXT,
    last_logon_utc          TEXT,
    password_expires_utc    TEXT,
    account_locked          INTEGER NOT NULL DEFAULT 0,
    last_seen_utc           TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_client_accounts_ou ON client_accounts(ou_dn);
CREATE INDEX IF NOT EXISTS ix_client_accounts_dept ON client_accounts(department);
```

`is_admin` est le champ structurant : le prototype (`docs/preview.html` l. 981-988) exige que les
comptes administrateurs **ne soient pas** ciblés par les GPO utilisateur. Le calcul doit être fait
**côté serveur AD** (via `Get-ADPrincipalGroupMembership` / `memberOf`), pas côté UI.

### 4.6 Vue « Comptes clients » (E3)

```
frontend/ADOps.GpoManager.App/
├── ViewModels/ClientAccountsViewModel.cs      (NOUVEAU)
├── Views/ClientAccountsView.xaml              (NOUVEAU)
├── ViewModels/AdminToolsViewModel.cs          (NOUVEAU ou section de ClientAccountsView)
└── Services/IAdminToolsLauncher.cs            (NOUVEAU)
```

Contenu de la vue :

1. **Table des comptes** : nom de connexion, nom complet, département, OU, dernier logon, état
   (Activé / Désactivé / Verrouillé), badge **Administrateur** vs **Standard**, GPO appliquées (RSoP lu).
2. **Filtres** : recherche, département, « standards uniquement », « désactivés », « jamais connectés ».
3. **Volet « Outils d'administration »** : liste des outils trouvés dans
   `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Administrative Tools`, avec bouton
   « Ouvrir » → lance l'outil **en ciblant le compte/machine sélectionné**.
4. **Actions** : « Appliquer la GPO sélectionnée à ce compte », « Voir l'OU », « Ouvrir dans ADUC ».

### 4.7 Intégration des outils d'administration (E3)

Le dossier `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Administrative Tools` contient des
**raccourcis `.lnk`** (et des fichiers `.msc`) vers les consoles MMC :

| Outil typique | Cible | Cible utilisable |
|---|---|---|
| Utilisateurs et ordinateurs Active Directory | `dsa.msc` | `dsa.msc /dc=<DC-FQDN>` |
| Ordinateurs et services Active Directory | `dsr.msc` | `dsr.msc /dc=<DC-FQDN>` |
| Gestion de l'ordinateur | `compmgmt.msc` | `compmgmt.msc` |
| Gestion des stratégies de groupe | `gpedit.msc` | `gpedit.msc` |
| Gestion des quotas de volumes | `disksnap.msc` | — |
| Utilisateurs et computers (local) | `lusrmgr.msc` | — |
| Services | `services.msc` | — |

**Deux stratégies d'implémentation :**

| | A — énumérer les `.lnk` du dossier | B — table de correspondance code → cible |
|---|---|---|
| Principe | Lister le dossier, résoudre chaque `.lnk` (parsing binaire `ShellLink` ou `IShellLink` COM) | Déclarer en dur les outils attendus et leur cible (`dsa.msc`…) |
| Avantage | Reflète **exactement** ce qui est installé sur la VM | Zéro dépendance COM, déterministe, testable |
| Inconvénient | Parsing `.lnk` non trivial (format binaire Microsoft) | Ne détecte pas un outil ajouté hors standard |
| Verdict | À réserver | **Retenu** — avec un fallback qui vérifie l'existence du binaire cible |

**Recommandation : stratégie B**, avec vérification d'existence de la cible dans
`%SystemRoot%\System32` avant de proposer l'outil, et un **blocage gracieux** (« *outil non
installé sur cette VM* ») plutôt qu'une exception. Complément possible : un bouton « Dossier des
outils d'administration » qui ouvre l'explorateur à l'emplacement exact — utile quand l'admin veut
juste retrouver le raccourci.

> Point d'attention : ces outils sont des **MMC snap-ins 32 bits**. Sur une VM 64 bits, `dsa.msc`
> se lance via `%SystemRoot%\SysWOW64\dsa.msc` si `System32\dsa.msc` est absent. La table de
> correspondance doit donc tester plusieurs chemins.

### 4.8 Mode hors-ligne (E5)

```
IInventorySyncService / SyncOrchestrator
        │
        ├──_cache local (SQLite)──►  Vue (lecture)        ← toujours disponible
        │
        └──_AD DS / WinRM────────►  Écriture (New-GPLink) ← bloquée si hors-ligne
```

TroisPiliers :

1. **Cache complet** : `client_accounts` + `computers` + `gpos` + `gpo_links` + `organizational_units`
   → le cache est un instantané complet et exploitable.
2. **Bannière d'état** : `ConnectionState` gagne `IsOffline` + `LastSnapshotUtc` ; la barre d'état
   affiche « *Hors-ligne — instantané du 22/10 14:32 (il y a 2 h)* ».
3. **Écritures désactivées** : `IGpoDeploymentService.DeployAsync` refuse d'exécuter si
   `context.IsOffline` (échec explicite et audité, jamais un silence).

Détection du mode hors-ligne : échec du health check **au démarrage** → bascule en lecture seule avec
le cache. Ou bascule manuelle via `Paramètres ▸ Mode hors-ligne`.

### 4.9 Application aux machines clientes (E6)

Extension de `IGpoDeploymentService` (le moteur existe, il est déjà planifié, audité et idempotent) :

```csharp
// Core/Services/IGpoDeploymentService.cs  (extension)
public interface IGpoDeploymentService
{
    // … signatures existantes conservées (PlanDeploymentAsync, DeployAsync, UnlinkGpoAsync)

    // NOUVEAU : plan de déploiement exprimé en « machines », pas en « OU »
    Task<OperationResult<MachineGpoDeploymentPlan>> PlanMachineDeploymentAsync(
        DomainContext context,
        IReadOnlyList<GpoSummary> selectedGpos,
        IReadOnlyList<ComputerInfo> targetComputers,
        IReadOnlyList<UserInfo> clientAccounts,
        IReadOnlyList<GpoLinkInfo> existingLinks,
        CancellationToken cancellationToken = default);

    // NOUVEAU : application + rafraîchissement + vérification
    Task<OperationResult<MachineGpoDeploymentResult>> DeployToMachinesAsync(
        DomainContext context,
        MachineGpoDeploymentPlan plan,
        bool forceRefreshOnTargets,     // gpupdate /force sur chaque poste
        bool verifyAfterApply,          // relecture gpresult /r + regkey
        CancellationToken cancellationToken = default);
}
```

Le plan calcule :
- le **regroupement par OU** des machines sélectionnelles → N liens (GPO × OU) au lieu de N × M ;
- la **classification admin/standard** → proposition de filtre de sécurité ;
- l'**impact** en utilisateurs/ordinateurs (la logique `IsInSubtree` de `GpoDeploymentService.cs:372-375`
  est déjà correcte et réutilisable).

Nouvelle commande obligatoire pour la vérification :

```powershell
# InvokeGpUpdateCommand (exécuté via WinRM sur le poste cible)
& { gpupdate.exe /force /target:computer; exit $LASTEXITCODE }

# GetGpResultPerComputerCommand (via WinRM sur le poste cible)
& { gpresult.exe /r /scope:computer }
```

> **Contrainte réelle** : ces deux commandes exigent WinRM **sur le poste client**. Sur la VM de test,
> il faut `Enable-PSRemoting -Force` sur le client (et non sur le seul DC). Sans cela, la vérification
> post-application est impossible et il faut s'arrêter à l'étape « liens créés ».

---

## 5. Plan de livraison (lots ordonnés)

Chaque lot est **indépendant et vérifiable** sur la VM.

### P0 — Débrayage de la connexion (E1) — *préalable bloquant*

| Fichier | Action |
|---|---|
| `App.xaml.cs` | Remplacer l'ouverture de `LoginWindow` par l'auto-session (§ 4.2) |
| `Views/MainWindow.xaml.cs` | Remplacer la ré-auth par le déverrouillage local (§ 4.3) |
| `ViewModels/SettingsViewModel.cs:215` | Supprimer « Changer de connexion » → remplacer par « Rejouer la détection de session » |
| `Bootstrapper.cs` | Retirer `ILoginWindowService`, `LoginWindow` |
| `Views/LoginWindow.xaml(.cs)`, `ViewModels/LoginViewModel.cs` | **Supprimer** (ou archiver dans `docs/legacy/`) |
| `Core/Network/SessionIdentity.cs` (NOUVEAU) | Identité de session |
| `Core/Services/ISessionBootstrapper.cs` + `ActiveDirectory/Services/SessionBootstrapper.cs` (NOUVEAU) | Auto-détection |
| `Core/Security/` | Contrôle d'élévation + vérification du groupe admin du domaine au démarrage |

⚠️ **Critère de sortie** : l'exe s'ouvre **directement** sur le tableau de bord, affiche
« *Session implicite : DOMAINE\admin (élevé) — DC dc1.lab.local* », et **refuse** d'agir si la session
n'est pas élevée ou si le compte n'a aucun droit sur les GPO.

### P1 — Cache comptes clients + synchronisation complète (E4 + socle E5)

| Fichier | Action |
|---|---|
| `Infrastructure/Storage/DatabaseSchema.cs` | + `client_accounts`, + `sync_steps`, + **mécanisme de version de schéma** (voir § 6) |
| `Infrastructure/Storage/InventoryRepository.cs` / `InventoryQueryRepository.cs` | Persister / lire les comptes clients |
| `Core/Commands/ListClientAccountsCommand.cs` (NOUVEAU) | `Get-ADUser` enrichi (groupes, admin, expiration, verrouillage) |
| `Core/Models/ClientAccount.cs` (NOUVEAU) | Modèle |
| `ActiveDirectory/Services/ActiveDirectoryInventoryService.cs` | Ajouter la lecture des comptes clients |
| `Core/Models/SyncResult.cs` / `SyncRunSummary.cs` | + `UsersRead`, `AccountsRead` |

⚠️ **Critère de sortie** : après synchronisation, le cache contient les comptes ; l'application
redémarre **sans le DC joignable** et la liste des comptes s'affiche avec l'âge de l'instantané.

### P2 — Synchroniseur à étapes + bouton global (E2)

| Fichier | Action |
|---|---|
| `Core/Services/ISyncOrchestrator.cs`, `ActiveDirectory/Services/SyncOrchestrator.cs` (NOUVEAU) | 7 étapes, `Progress<SyncStepResult>` |
| `Core/Models/SyncStepResult.cs` (NOUVEAU) | Modèle d'étape (réutiliser le rendu de `HealthCheckStepViewModel`) |
| `ViewModels/MainViewModel.cs` | `SyncNowCommand` + état (en cours / terminé / erreurs) |
| `Views/MainWindow.xaml` | Bouton **« ⟳ Synchroniser »** dans la barre supérieure ou la barre d'état |
| `ViewModels/DashboardViewModel.cs` | Carte « Dernière synchronisation » + bouton + détail des étapes |
| `ViewModels/ParcViewModel.cs` | Remplacer le bouton local par le global (garder l'alias si l'UX le demande) |

⚠️ **Critère de sortie** : un clic → les 7 étapes s'affichent en direct, la barre de progression avance,
le cache est mis à jour, `LastSyncText` et l'audit sont à jour.

### P3 — Vue « Comptes clients » + outils d'administration (E3)

| Fichier | Action |
|---|---|
| `Core/Services/IClientAccountsService.cs` + impl (NOUVEAU) | Lecture cache + live |
| `ViewModels/ClientAccountsViewModel.cs` + `Views/ClientAccountsView.xaml` (NOUVEAU) | Table + filtres + actions |
| `Core/Services/IAdminToolsService.cs` + impl (NOUVEAU) | Table de correspondance + vérification d'existence |
| `Services/IAdminToolsLauncher.cs` (NOUVEAU) | `Process.Start` du MMC ciblé |
| `App.xaml` | `DataTemplate` ClientAccountsViewModel → ClientAccountsView |
| `Views/MainWindow.xaml` | Entrée de navigation « Comptes clients » |
| `Bootstrapper.cs` | Enregistrement des nouveaux services |

⚠️ **Critère de sortie** : la vue liste les comptes (tri/filtre/badges admin), et « Ouvrir dans ADUC »
lance `dsa.msc /dc=dc1.lab.local` sur la VM ; un outil absent est signalé proprement.

### P4 — Application aux machines clientes (E6)

| Fichier | Action |
|---|---|
| `Core/Commands/InvokeGpUpdateCommand.cs`, `GetGpResultPerComputerCommand.cs` (NOUVEAU) | Refresh + vérification |
| `Core/Commands/SetGpoSecurityFilterCommand.cs` (NOUVEAU) | Filtrage par groupe (exclusion des admins) |
| `Core/Services/IGpoDeploymentService.cs` + `ActiveDirectory/Services/GpoDeploymentService.cs` | `PlanMachineDeploymentAsync` + `DeployToMachinesAsync` |
| `Core/Models/GpoDeploymentModels.cs` | `MachineGpoDeploymentPlan` / `…Result` |
| `ViewModels/DeploymentsViewModel.cs`, `ViewModels/AutomateGpoViewModel.cs` | Ciblage « machines » + progression par poste |

⚠️ **Critère de sortie** : 2 machines sélectionnées + 1 GPO → 1 lien créé sur l'OU, `gpupdate /force`
renvoyé sur chaque poste, `gpresult /r` relu et affiché, le compte admin **exclu**, l'audit complet.

### P5 — Documentation & recette

- `README.md` : refonte des sections « Installation », « Connexion » (supprimée) → « Session implicite ».
- `docs/07` : **réécrire** (le mode démo décrit n'existe pas) → recette réelle sur VM.
- `docs/09` : corriger le § 6 (renvoi au mode démo inexistant).
- Créer `docs/06-Mode-Demo-Local.md` **ou** supprimer toutes ses références (choix à trancher).
- `docs/12` : *Procédure de recette VM* (créer le compte de test, activer WinRM sur le client,
  vérifier le « Bob vs Administrateur »).

---

## 6. Risques, limites et points de vigilance

| # | Risque | Impact | Mitigation |
|---|--------|--------|------------|
| R1 | **Suppression du login = perte de la barrière de sécurité** | Un poste non autorisé peut lier des GPO | Contrôle de préconditions au démarrage : élévation **et** droit sur le domaine ; refus explicite + audit |
| R2 | `gpupdate` / `gpresult` exigent **WinRM sur le client** | La vérification post-application est impossible | Prérequis explicite dans la recette ; sinon s'arrêter à « liens créés » |
| R3 | La GPO s'applique à une **OU**, pas à un objet | Le ciblage « par machine » n'est pas natif | Regroupement par OU + filtre de sécurité par groupe ; le dire dans l'UI |
| R4 | **Cycle de rafraîchissement GPO** (≈ 90 min ± 30) | L'utilisateur ne voit pas l'effet immédiatement | `gpupdate /force` explicite + bannière « délai naturel : ~90 min » + RSoP horodaté |
| R5 | **Version de schéma SQLite** | Une base existante + un nouveau `CREATE TABLE` = ok, mais les futures evolutions cassent | Introduire un `schema_version` (PRAGMA `user_version`) **dès P1** |
| R6 | **`Get-ADUser -Filter *`** sur un grand domaine | Lent / mémoire | `LDAPFilter` par OU + pagination ; `SyncResult.Warnings` déjà prévu |
| R7 | Le **filtre de sécurité** est une modification de GPO | Peut casser la production | Confirmation explicite + audit + ordre « ne pas forcer » (Link Enabled) |
| R8 | `dsa.msc` est un **snap-in 32 bits** | Absent de `System32` sur les OS 64 bits | Tester `%SystemRoot%\SysWOW64\dsa.msc` en second |
| R9 | Compilation **impossible sur la machine d'analyse** | Risque de régression non détectée | Build + `dotnet test` **sur la VM** avant toute publication |
| R10 | Le `publish\ADOps.GpoManager.App.exe` présent est **antérieur** aux modifications | Tester un binaire périmé | Toujours regénérer via `publish.bat` + `--generate-integrity-manifest` |

---

## 7. Décisions à trancher (5 questions)

1. **Hors-ligne = (a) sans Internet ou (b) sans annuaire ?** → Recommandation : **(b)**, cache-first + mode dégradé.
2. **Le ciblage « machines clientes »** : liens par OU regroupée (recommandé, natif GPO) ou
   filtrage de sécurité par groupe contenant les machines ciblées ?
3. **Le filtre de sécurité excluant les comptes administrateurs** : est-il dans le périmètre ?
   (le prototype `docs/preview.html` l. 981-988 l'exige ; le README le liste comme limite actuelle)
4. **Faut-il conserver un accès « avancé » à la connexion** (ré-authentification vers un DC distant,
   compte explicite) pour les postes **non joints** au domaine ?
   → Sans lui, l'application ne fonctionne que sur un poste joint. Recommandation : **oui, garder
   une connexion « optionnelle » dans Paramètres**, mais **sans écran bloquant** — cohérent avec E1
   (« retirer l'écran de connexion » ≠ « interdire toute autre configuration »).
5. **Les outils d'administration** : table de correspondance codée en dur (recommandé) ou énumération
   dynamique des `.lnk` ?

---

## 8. Synthèse

| Exigence | Charge estimated | Lots |
|----------|-----------------|------|
| E1 Suppression connexion | 3 j | P0 |
| E4 Synchronisation complète | 4 j | P1 |
| E2 Bouton Synchroniser global | 2 j | P2 |
| E5 Mode hors-ligne (cache-first) | 2 j | P1 + P2 |
| E3 Vue comptes clients + outils admin | 5 j | P3 |
| E6 Application aux machines | 6 j | P4 |
| Documentation / recette | 2 j | P5 |

**Total ≈ 24 jours-homme** (hors recette sur VM). Le socle technique est déjà solide : l'architecture
en couches, la DI, l'audit SQLite, les commandes typées et le moteur de déploiement existent et sont
testés. Le travail porte sur **l'orchestration**, **le cache** et **l'ergonomie** — pas sur une
réécriture.

> **Point d'attention final** : la compilation et les tests doivent être exécutés sur la VM
> (SDK .NET 10 absent de la machine d'analyse). Aucun binaire présent dans `publish/` ne doit être
> considéré comme reflétant l'état du code.
