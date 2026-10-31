# Recette sur VM (Hyper-V) — application ADOps GPO Manager

Date de révision : 22 octobre 2026. Périmètre : sources du projet `gpo-main`
(session implicite, synchronisation immédiate, comptes clients, outils
d'administration) et procédure de recette sur la VM de test.

> **Correction majeure (22/10/2026).** La version précédente de ce document
> décrivait un « mode démo » intégré à l'application (compte `demo`, mot de
> passe `DemoOnly2026!`, exécuteur PowerShell simulé, données AD fictives) et
> une documentation `docs/06-Mode-Demo-Local.md`.
> **Ces éléments n'existent pas dans le code** : aucune occurrence de
> `DemoOnly2026` / `demo.local` / `DemoMode` dans les sources, et
> `docs/06-Mode-Demo-Local.md` est absent du dépôt. Le seul mode « démo »
> disponible est le **prototype HTML** `docs/preview.html` (navigateur, sans
> contact avec AD DS).
> Le document ci-dessous décrit le comportement **réel** de l'application.

## 1. Ce que fait l'application au démarrage

1. Ouverture directe de la fenêtre principale — **aucun écran de connexion**.
2. Détection de la session Windows : compte, élévation.
3. Détection du domaine et du contrôleur (API Netlogon `DsGetDcName`).
4. Health Check en 6 étapes (DNS · RSAT · WinRM · authentification · AD DS · GroupPolicy).
5. **Synchronisation immédiate** : OU, ordinateurs, **comptes clients**, GPO, liens.
6. Rétroaction en direct dans la barre supérieure + barre d'état.

## 2. Prérequis sur la VM

| Composant | Installation | Vérification |
|---|---|---|
| SDK .NET 10 (compilation) | `winget install Microsoft.DotNet.SDK.10` | `dotnet --version` |
| RSAT — module `GroupPolicy` | `Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0` | `Get-Module -ListAvailable GroupPolicy` |
| RSAT — module `ActiveDirectory` | `Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0` | `Get-Module -ListAvailable ActiveDirectory` |
| WinRM sur le DC (mode distant) | `Enable-PSRemoting -Force` | `Test-WSMan -ComputerName dc1.domaine.local` |
| Droits | Membre de **Domain Admins** (ou délégation GPMC) | — |

> La session doit être **élevée** : `New-GPLink` l'exige. L'application
> signale explicitement l'absence d'élévation.

## 3. Recette pas à pas

1. **Compiler** sur la machine disposant du SDK .NET 10 :
   ```powershell
   dotnet restore .\ADOps.GpoManager.sln
   dotnet build   .\ADOps.GpoManager.sln -c Release
   dotnet test    .\ADOps.GpoManager.sln -c Release
   ```
2. **Publier** (ne jamais tester un exécutable antérieur aux modifications) :
   ```powershell
   .\publish.bat
   publish\ADOps.GpoManager.App.exe --generate-integrity-manifest
   ```
3. Lancer `publish\Lancer-Application.cmd` **en tant qu'administrateur**.
4. Vérifier qu'**aucun écran de connexion** n'apparaît : la fenêtre principale
   s'ouvre directement.
5. Vérifier la barre supérieure :
   - `SESSION` → « Session implicite : DOMAINE\admin (élevé) — domaine
     lab.local via dc1.lab.local » ;
   - un avertissement est affiché si la session n'est pas élevée ;
   - le message de synchronisation énumère les volumes collectés
     (OU, ordinateurs, comptes clients, GPO, liens).
6. Vérifier la **page « Comptes clients »** : la liste des comptes est peuplée,
   le filtre « Standards uniquement » exclut les comptes d'administration, la
   recherche et le filtre par département fonctionnent.
7. Vérifier les **outils d'administration** : « Ouvrir ADUC » lance la console
   ADUC sur le contrôleur détecté ; le dossier « Outils d'administration » du
   menu Démarrer s'ouvre et liste les raccourcis réellement installés.
8. Cliquer **⟳ Synchroniser** : le message de synchronisation se met à jour,
   l'entrée d'audit `InventorySync` est créée, l'horodatage de la barre d'état
   change.
9. Vérifier la **persistance** : fermer l'application, la relancer — le cache
   local est rechargé (table `client_accounts` incluse). Couper le réseau
   (ou arrêter le DC) et relancer : l'application s'ouvre en **mode dégradé**,
   la consultation du cache fonctionne, et un message explique l'absence de
   domaine.
10. **Verrouillage** : laisser la console inactive (10 min par défaut) — la
    fenêtre est masquée et une fenêtre de déverrouillage est demandée ;
    « Déverrouiller » réaffiche la console, « Fermer l'application » quitte.

## 4. Contrôles effectués et non effectués

| Contrôle | Résultat |
|---|---|
| Analyse XML des vues XAML du projet | Fichiers bien formés après correction |
| Équilibre des accolades / structure C# des fichiers modifiés | Contrôlé statiquement (pas de compilateur sur la machine d'analyse) |
| `dotnet restore` / `build` / `test` | **À exécuter sur la machine disposant du SDK .NET 10** |
| Exécution de l'application modifiée | À faire sur la VM après build |
| Intégration réelle AD DS / WinRM | À faire sur la VM |

> **Point d'attention** : le SDK .NET 10 n'était pas installé sur la machine
> d'analyse. Aucune compilation n'a donc pu être réalisée là-bas : le build et
> les tests **doivent** être exécutés avant toute publication.

## 5. Voir aussi

- [`05-Lab-VMware.md`](05-Lab-VMware.md) — mise en route de la plateforme de test
- [`09-Sync-et-Application-Reelle.md`](09-Sync-et-Application-Reelle.md) — flux de synchronisation et d'application
- [`10-Feuille-de-Route-Production.md`](10-Feuille-de-Route-Production.md) — mise en service progressive
- [`11-Cahier-des-Charges-VM-Architecture-Cible.md`](11-Cahier-des-Charges-VM-Architecture-Cible.md) — diagnostic et architecture cible
