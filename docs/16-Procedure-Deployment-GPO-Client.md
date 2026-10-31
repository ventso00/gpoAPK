# Déploiement des GPO et validation client

Ce document décrit la **procédure de résolution** appliquée pour faire en sorte
que les **16 modèles du catalogue ADOps** déploient réellement et **s'appliquent**
sur les **postes clients** du domaine Active Directory `sarytany.local`, pour les
**utilisateurs standard ciblés** (`henry`, `jaona`, `tanjona`, `ventso`, …), sans
nécessiter d'intervention manuelle sur les postes clients.

---

## 1. Objectif

| Exigence | État final |
|---|---|
| Les GPO du catalogue sont **matérialisées** dans le domaine | ✅ 16 GPO créées / mises à jour |
| Les GPO sont **liées** aux bons OUs | ✅ 20 liens de GPO |
| Les GPO de **portée Ordinateur** s'appliquent aux postes clients | ✅ via **Loopback Merge** |
| Les GPO sont **filtrées** aux utilisateurs standard ciblés | ✅ groupe AD DS `ADOps-*` |
| Les comptes **administrateurs** ne reçoivent pas les GPO standard | ✅ `GpoApply` jamais accordé aux admins |
| Déploiement terminé avec **exit code 0** | ✅ 16/16 modèles OK |

---

## 2. Architecture de déploiement — ciblage hybride

Le déploiement n'est **pas** effectué poste par poste (les clients du lab ne sont
pas joignables en WinRM) : il est **serveur‑côté** (contrôleur de domaine) et se
fait en deux phases, toutes deux pilotées par l'application :

1. **Matérialisation** (`ApplyGpoTemplate`) : création ou mise à jour de la GPO
   (`New-GPO`, `Set-GPRegistryValue`, nettoyage des valeurs obsolètes).
2. **Ciblage AD DS** (`SetGpoTargeting`) : création du groupe de sécurité
   `ADOps-<slug>`, synchronisation des membres, **remplacement du filtrage de
   sécurité** de la GPO.

### 2.1 Phase 1 — Matérialisation

```powershell
Import-Module GroupPolicy -ErrorAction Stop
$gpo = Get-GPO -Name '<NomDeLaGPO>' -ErrorAction SilentlyContinue
$created = $false
if ($null -eq $gpo) {
    $gpo = New-GPO -Name '<NomDeLaGPO>' -Comment '<comment>' -ErrorAction Stop
    $created = $true
} elseif ('<comment>'.Length -gt 0) {
    $gpo.Description = '<comment>'   # « Set-GPO » n'existe PAS dans le module GroupPolicy
}
Remove-GPRegistryValue -Guid $gpo.Id -Key '<key>' -ValueName '<value>' -ErrorAction SilentlyContinue | Out-Null
Set-GPRegistryValue -Guid $gpo.Id -Key '<key>' -ValueName '<value>' -Type DWord -Value 1 -ErrorAction Stop | Out-Null
$gpo = Get-GPO -Guid $gpo.Id -ErrorAction Stop
[PSCustomObject]@{ GpoId = $gpo.Id.ToString(); DisplayName = $gpo.DisplayName; Created = $created }
```

> **Piège corrigé** : le module GroupPolicy **n'expose pas** de cmdlet
> `Set-GPO`. La description d'une GPO existante se pose via la propriété
> `Description` de l'objet retourné par `Get-GPO`
> (`$gpo.Description = '…'`). Utiliser `Set-GPO` laissait la commande silencieuse
> et la description non appliquée.

> **Nettoyage** : avant `Set-GPRegistryValue`, les valeurs précédentes sont
> supprimées (`Remove-GPRegistryValue`) afin qu'une URL qui n'est plus listée ne
> reste pas bloquée.

### 2.2 Phase 2 — Filtrage AD DS et liaison

```powershell
Import-Module ActiveDirectory -ErrorAction Stop
Import-Module GroupPolicy -ErrorAction Stop
$gpo = Get-GPO -Guid '<gpo-guid>' -ErrorAction Stop

$group = $null
try { $group = Get-ADGroup -Identity '<ADOps-slug>' -ErrorAction Stop } catch { $group = $null }
if ($null -eq $group) {
    $domain = Get-ADDomain -ErrorAction Stop
    $group = New-ADGroup -Name '<ADOps-slug>' -SamAccountName '<ADOps-slug>'
        -GroupScope Global -GroupCategory Security
        -Path ('CN=Users,' + $domain.DistinguishedName)
        -Description '<desc>' -PassThru -ErrorAction Stop
}

$desired = @('<DN-usere-1>', '<DN-usere-2>', …)
$current = @(Get-ADGroupMember -Identity $group -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DistinguishedName)
foreach ($dn in $current) { if ($desired -notcontains $dn) { Remove-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop } }
$current = @(Get-ADGroupMember -Identity $group -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DistinguishedName)
foreach ($dn in $desired) { if ($current -notcontains $dn) { Add-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop } }

# Authenticated Users : lecture seule
$authUsers = Get-GPPermission -Guid $gpo.Id -All -ErrorAction SilentlyContinue | Where-Object { $_.Trustee.Sid.Value -eq 'S-1-5-11' } | Select-Object -First 1
if ($null -ne $authUsers) {
    Set-GPPermission -Guid $gpo.Id -TargetName $authUsers.Trustee.Name -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue
} else {
    Set-GPPermission -Guid $gpo.Id -TargetName 'Authenticated Users' -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue
}

# Domain Computers : lecture (téléchargement) + application (portée Ordinateur)
$domainComputers = $null
try {
    $domainSid = (Get-ADDomain -ErrorAction Stop).DomainSID.Value
    $domainComputers = Get-ADGroup -Identity ($domainSid + '-515') -ErrorAction Stop
} catch { $domainComputers = Get-ADGroup -Filter "Name -eq 'Domain Computers' -or SamAccountName -eq 'Domain Computers'" -ErrorAction SilentlyContinue | Select-Object -First 1 }
if ($null -ne $domainComputers) {
    Set-GPPermission -Guid $gpo.Id -TargetName $domainComputers.Name -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue
    if (<ApplyToComputers>) { Set-GPPermission -Guid $gpo.Id -TargetName $domainComputers.Name -TargetType Group -PermissionLevel GpoApply -Replace -ErrorAction Stop }
}

# Domain Admins : édition uniquement (pas d'application)
$domainAdmins = $null
try { $domainAdmins = Get-ADGroup -Identity ((Get-ADDomain -ErrorAction Stop).DomainSID.Value + '-512') -ErrorAction Stop } catch { }
if ($null -ne $domainAdmins) {
    Set-GPPermission -Guid $gpo.Id -TargetName $domainAdmins.Name -TargetType Group -PermissionLevel GpoEditDeleteModifySecurity -Replace -ErrorAction SilentlyContinue
}

Set-GPPermission -Guid $gpo.Id -TargetName '<ADOps-slug>' -TargetType Group -PermissionLevel <GpoRead|GpoApply> -Replace -ErrorAction Stop
```

### 2.3 Boucle des membres

- `Get-ADGroupMember … -ErrorAction SilentlyContinue` → `Remove-ADGroupMember` pour
  les membres désormais absents.
- `Add-ADGroupMember` pour les nouveaux membres.
- `Set-GPPermission … -Replace` est **indispensable** : sans lui, les permissions
  déjà existantes (ex. `GpoApply`) ne sont **pas rétrogradées.

### 2.4 Choix du `PermissionLevel`

| Portée | Groupe cible | `GpoRead` | `GpoApply` | Administrateurs |
|---|---|---|---|---|
| Utilisateur | Groupe `ADOps-*` (utilisateurs standards ciblés) | ✅ | — | `GpoEditDeleteModifySecurity` |
| Ordinateur | `Domain Computers` (via groupe cible) | ✅ | ✅ | `GpoEditDeleteModifySecurity` |

- **Portée Utilisateur** : le groupe `ADOps-*` reçoit `GpoApply` → la stratégie
  s'applique aux utilisateurs membres du groupe.
- **Portée Ordinateur** : `Domain Computers` reçoit `GpoRead` **+** `GpoApply`
  → la configuration ordinateur est téléchargée et appliquée par les postes
  clients du domaine.

### 2.5 Synchronisation AD DS préalable au déploiement

Avant toute tentative de déploiement, l'application **force une synchronisation
complète** de l'inventaire AD DS :

1. Interrogation directe du contrôleur de domaine (`ventso.sarytany.local`) via
   l'exécuteur local (RSAT) ou WinRM.
2. Collecte de la liste complète des **comptes utilisateurs actifs**, des
   **machines / ordinateurs** du domaine et de la **structure des OU**.
3. Persistance du cache local puis **rechargement des cibles**.
4. Puis, seulement : matérialisation, filtrage et liaison des GPO.

Cette étape est exécutée **dans le flux de déploiement** de l'assistant
(`GpoAssistantViewModel.ExecuteAsync` → `IInventorySyncService.SyncAsync` →
rechargement des cibles). Elle garantit qu'aucune donnée absente ou obsolète (cache
vide / périmé) ne conduit à un « 0 poste sélectionné » avant la matérialisation et
la liaison des GPO.

---

## 3. Loopback processing — mode **Merge**

Pour les modèles dont les paramètres sont **machine** (audit avancé, blocage Web,
USB, autorun, blocage PowerShell, restrictions shell, sécurité réseau, UAC,
SmartScreen, ASR, …), la GPO est créée en **portée Ordinateur** mais doit être
appliquée **au nom de l'utilisateur** qui ouvre une session.

Le mécanisme : **Loopback processing** — configuration utilisateur
**Group Policy** → **Administrative Templates** → **System** → **Group Policy** →
**User Group Policy loopback processing mode**.

| Valeur | Signification |
|---|---|
| **1** | **Merge** — appliquer la GPO utilisateur sur les paramètres machine de l'utilisateur connecté |
| **2** | Replace — remplacer entièrement la stratégie utilisateur |

> **Décision** : le mode **Merge (1)** est utilisé. Le service positionne
> automatiquement `UserPolicyMode = 1` pour toute GPO de portée Ordinateur via
> l'instruction interne `WithLoopbackMerge` (idempotent).

> Sans loopback (ou en mode Replace), une GPO créée en portée Ordinateur ne
> s'applique pas aux postes clients pour un utilisateur standard : le déploiement
> était donc fonctionnel côté AD mais **invisible côté client**.

---

## 4. Filtrage et exclusion des administrateurs

- Les administrateurs du domaine (`Domain Admins`, `Enterprise Admins`) ne
  reçoivent **jamais** `GpoApply` : ils conservent uniquement
  `GpoEditDeleteModifySecurity` (droits d'édition de la GPO, pas d'application).
- Le groupe `Authenticated Users` n'a que `GpoRead` : sans `GpoApply`, aucun
  compte ne peut recevoir la stratégie.
- Les comptes standards ciblés sont **uniquement** membres du groupe `ADOps-*` :
  un compte administrateur qui ne serait pas dans le groupe n'y serait pas non
  plus appliqué.

---

## 5. Procédure de validation côté client

### 5.1 Prérequis

- Le poste client est **joint au domaine** `sarytany.local` et le compte utilisateur
  standard est **activé** dans AD DS.
- La GPO est **liée** à l'OU attendue (les GPO utilisateurs sont liées dans les OUs
  des utilisateurs ; les GPO machines via les OUs des postes clients).

### 5.2 Étapes

```powershell
gpupdate /force
gpresult /r /scope computer
```

1. **`gpupdate /force`** : force la relecture de la stratégie (Machine et/ou
   Utilisateur) et déclenche l'application des GPO liées.
2. **`gpresult /r /scope computer`** : affiche le RSoP (Resultant Set of Policy)
   **côté machine**. Avec le loopback Merge, le scope `computer` reflète bien les
   GPO de type machine propres au poste **plus** celles qui leur sont fusionnées
   via loopback depuis la stratégie utilisateur.

   Pour une GPO de **portée Utilisateur** appliquée via loopback, la lecture
   fiable se fait côté **User** (`gpresult /r /scope user` côté session utilisateur)
   ou via `Get-GPInheritance` côté contrôleur.

3. Vérifier dans le RSoP que les 16 modèles attendus apparaissent bien avec le bon
   `Applied` / `Denied` / `Failed`.
4. Vérifier l'onglet **Paramètres** de la GPO pour s'assurer que les valeurs de
   registre attendues sont bien présentes (ex. `Deny_All = 1` pour le stockage USB).
5. Vérifier le **journal des événements** (`Event Viewer` → `Applications and
   Services Logs` / `Microsoft\Windows\GroupPolicy\Operational`) et le journal
   fichier `%LOCALAPPDATA%\ADOps\GpoManager\logs\adops.log`.

### 5.3 Vérifications post-application

- **Côté poste** : `gpresult /r` (machine puis user), `gpresult /h <fichier>.html`
  pour une trace lisible.
- **Côté contrôleur** : `Get-GPO -Id <gpo-guid> | Select-Object DisplayName,Enabled,Link`
  et `Get-GPLink -Target '<DN-OU>' -Name <gpoName>`.
- **Côté registre** : `Get-ItemProperty '<key>' -Name <value>`.

---

## 6. Dépannage

| Symptôme | Cause probable | Correctif |
|---|---|---|
| `gpupdate /force` OK, aucun effet | GPO liée à l'OU erronée | Vérifier le lien `Get-GPLink` |
| `gpresult` montre `Denied` | Filtre de sécurité insuffisant (`GpoRead` manquant) | `Set-GPPermission … -Replace` |
| GPO machine bien présente mais non appliquée | Loopback absent / mode Replace | Positionner `UserPolicyMode = 1` |
| Seuls les admins sont bloqués / aucun appliqué | `GpoApply` non accordé au groupe | Portée machine → `GpoApply` sur `Domain Computers` |
| Administrateurs reçoivent la GPO | `GpoApply` accordé aux admins | Ne jamais accorder `GpoApply` aux admins |

---

## 7. Rollback

- Retirer le lien GPO : `Remove-GPLink -Target '<DN-OU>' -Name '<GPO>'`.
- Réinitialiser le filtrage : remettre `GpoRead` sur le groupe cible / rétrograder
  `GpoApply` via `Set-GPPermission -Replace`.
- Restaurer le loopback : `Set-GPRegistryValue -Guid <gpo-id> -Key
  'HKLM\SOFTWARE\Policies\Microsoft\Windows\GroupPolicy' -ValueName
  'UserGroupPolicyLoopbackPolicyMode' -Type DWord -Value 0`.

---

## 8. Références

- [`docs/14-Controle-ADDS-des-Postes.md`](14-Controle-ADDS-des-Postes.md) — cycle
  de vie des postes, actions de contrôle AD DS.
- [`docs/08-GpoCatalog-Reference.md`](08-GpoCatalog-Reference.md) — référence
  exhaustive des 16 modèles (paramètres, clés de registre, portées).
- [`docs/09-Sync-et-Application-Reelle.md`](09-Sync-et-Application-Reelle.md) — flux
  de synchronisation et d'application réelle.
- [`docs/13-Refonte-Mode-Clair-et-Parcours-Simplifie.md`](13-Refonte-Mode-Clair-et-Parcours-Simplifie.md) —
  parcours simplifié en 4 étapes de l'assistant.
