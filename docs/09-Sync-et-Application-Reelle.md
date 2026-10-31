# Synchronisation et application réelle des GPO sur un domaine AD

## 1. Prérequis sur le poste d'administration

| Composant | Installation | Vérification |
|---|---|---|
| SDK .NET 10 (compilation) | `winget install Microsoft.DotNet.SDK.10` | `dotnet --version` |
| RSAT — module `GroupPolicy` | `Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0` | `Get-Module -ListAvailable GroupPolicy` |
| RSAT — module `ActiveDirectory` | `Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0` | `Get-Module -ListAvailable ActiveDirectory` |
| WinRM sur le DC (mode distant) | `Enable-PSRemoting -Force` sur le DC | `Test-WSMan -ComputerName dc1.domaine.local` |
| Droits | Compte membre de **Domain Admins** (ou délégation GPMC sur les OU ciblées) | — |
| Réseau | Accès au DC sur les ports 53 (DNS), 389/636 (LDAP), 5985/5986 (WinRM), 445 (SMB) | — |

> **Mode recommandé** : exécution **locale (RSAT)** avec le compte Windows courant
> administrateur du domaine (Kerberos). Le mode distant (WinRM) est réservé au
> compte administrateur explicite.

## 2. Flux de synchronisation

1. **Démarrage** : aucun écran de connexion. L'application lit la session
   Windows courante (identité, élévation) et détecte le domaine et le
   contrôleur via l'API Netlogon (`DsGetDcName`).
2. **Health Check** (`ISessionBootstrapper` → `IAuthenticationService`) vérifie
   DNS, modules RSAT, WinRM, authentification, accès AD DS et capacité
   GroupPolicy. En cas d'échec, l'application s'ouvre en mode dégradé et le
   message nomme le correctif (RSAT **ou** `Enable-PSRemoting`).
3. **Synchronisation immédiate** (`IInventorySyncService.SyncAsync`) :
   - lit les OU, **ordinateurs**, **comptes clients (utilisateurs)**, GPO et
     liens depuis AD DS ;
   - persiste dans le cache local SQLite (`audit.db`) — table `client_accounts`
     incluse, ce qui rend la consultation possible hors ligne ;
   - audite l'opération.
4. Le bouton **⟳ Synchroniser** (barre supérieure) relance la même collecte à
   la demande ; **Paramètres ▸ Rejouer la détection de session** rejoue la
   détection du domaine et du contrôleur.
5. Le cache local est la **source de vérité pour l'affichage** ; AD DS reste
   la source de vérité pour les modifications.

## 3. Application réelle d'une GPO

### A. Depuis le catalogue (création + application)
1. **Outils avancés → Catalogue de stratégies**.
2. Sélectionner un modèle (ex. « Restriction du Panneau de configuration »).
3. Cliquer **« Créer et appliquer cette GPO »**.
4. Le backend exécute le script PowerShell réel :
   ```powershell
   New-GPO -Name "Restriction Panneau de configuration" -Comment "NoControlPanel"
   Set-GPRegistryValue -Name "Restriction Panneau de configuration" `
       -Key "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" `
       -ValueName "NoControlPanel" -Type DWord -Value 1
   New-GPLink -Name "Restriction Panneau de configuration" `
       -Target "OU=Utilisateurs,DC=domaine,DC=local" -LinkEnabled Yes
   ```
5. L'opération est auditée (utilisateur Windows, horodatage UTC, cibles, résultat).

### B. Depuis la liste des GPO du domaine (liaison uniquement)
1. **Automatiser la GPO → Étape 2** (ou **Outils avancés → GPO du domaine**).
2. Cocher les GPO à appliquer.
3. Cliquer **« Appliquer »** à côté d'une GPO (ou passer par l'étape 3 pour le rapport d'impact).
4. `IGpoDeploymentService.PlanDeploymentAsync` valide la sélection et calcule
   l'impact ; `DeployAsync` crée les liens (`New-GPLink`).

### C. Détachement
- **Parc → double-clic sur une machine → Détacher** (`Remove-GPLink`).

## 4. Scénario de validation « Bob vs Administrateur »

1. Créer la GPO `control-panel-settings` (catalogue) → liée à l'OU
   `OU=Utilisateurs,DC=domaine,DC=local`.
2. Les comptes d'administration restent dans `OU=Administration` (hors portée).
3. Sur un PC client du domaine :
   - Connexion en tant que **Bob** (utilisateur standard) →
     `control.exe` / `ms-settings:` refusés avec le message d'interdiction.
   - Connexion en tant qu'**Administrateur** → accès complet.
4. Vérifier via `gpresult /r` (sur le poste client) que la GPO est appliquée
   pour Bob et absente pour l'administrateur.

## 5. Vérification de l'application

```powershell
# Sur le poste client :
gpresult /r | Select-String "Restriction Panneau"

# Sur le DC :
Get-GPO -Name "Restriction Panneau de configuration"
Get-GPResultantSetOfPolicy -Computer "PC-BOB" -User "bob" -ReportType Html -Path C:\rsop.html
```

## 6. Mode hors ligne (annuaire injoignable)

> **Note de révision (22/10/2026).** Cette section décrivait un « mode démo »
> (`demo` / `DemoOnly2026!`) avec un exécuteur PowerShell simulé. **Ce mode
> n'a jamais existé dans le code de l'application** : il n'existe que comme
> prototype HTML (`docs/preview.html`). Ne pas s'appuyer sur lui.

Le comportement réel quand le domaine est injoignable :

| Situation | Comportement |
|---|---|
| Démarrage, DC joignable | Session implicite établie + synchronisation immédiate. |
| Démarrage, DC **injoignable** | L'application **s'ouvre quand même** : la détection échoue avec un message actionnable, la barre supérieure affiche l'erreur, et l'interface reste consultable sur le **cache local** (dernière synchronisation). |
| Session non élevée | Avertissement explicite : la création de liens de GPO exige l'élévation. |
| Reprise | `Paramètres ▸ Rejouer la détection de session`, ou bouton **⟳ Synchroniser**. |

Le cache SQLite conserve les OU, les ordinateurs, **les comptes clients**, les
GPO et leurs liens : la consultation reste possible sans annuaire. En revanche,
**toute écriture** (création de lien, suppression de lien) exige un annuaire
joignable : elle est refusée explicitement et auditée, jamais silencieusement
ignorée.
