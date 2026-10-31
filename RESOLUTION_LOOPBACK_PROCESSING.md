# RÉSOLUTION — Déploiement des GPO de portée Ordinateur avec Loopback Processing (Merge)

> **Projet :** ADOps GPO Manager — WPF / MVVM / .NET 10  
> **Domaine :** `sarytany.local`  
> **Utilisateurs ciblés exemple :** `henry`, `jaona`, `tanjona`, `ventso`  
> **Objectif :** Garantir que toute GPO de type **Configuration Ordinateur** (Audit, Blocage Web, USB, Autorun…) s'applique impérativement sur les postes clients du domaine lorsque l'un des utilisateurs ciblés s'y connecte, et que le processus se termine avec un **code de sortie 0 (Succès)**.

---

## 1. PROBLÈME IDENTIFIÉ

L'application déployait des GPO contenant des paramètres **Configuration Ordinateur** en ne ciblant que des **comptes Utilisateurs** (0 poste sélectionné). Conséquence :

- La GPO est créée et liée, mais aucun objet machine n'a le droit de la lire ni de l'appliquer.
- Sur le poste client, `gpresult /r` ne liste pas la GPO (ou la liste sous "Refusés").
- Le déploiement se termine en échec (code de sortie 1).

**Cause racine :** une stratégie de groupe Configure-Ordinateur n'est traitée que par l'objet **ordinateur** au démarrage de la session machine. Si la GPO n'est liée qu'aux OU utilisateurs et filtrée uniquement sur des comptes utilisateurs, le poste client ne télécharge jamais les paramètres machine. De plus, sans **traitement en boucle (loopback)**, la configuration ordinateur n'est pas ré-évaluée à l'ouverture de session de l'utilisateur ciblé.

---

## 2. SOLUTION MISE EN ŒUVRE

Trois leviers sont activés conjointement dans `GpoTemplateDeploymentService.cs` et `SetGpoTargetingCommand.cs` :

### 2.1 Traitement en boucle (Loopback Processing) — mode "Merge" (Enregistrer)

Pour toute GPO de portée `PolicyScope.Computer`, la valeur de registre suivante est ajoutée **avant** la matérialisation de la GPO :

| Emplacement | Valeur | Type | Donnée | Signification |
|---|---|---|---|---|
| `HKLM\SOFTWARE\Policies\Microsoft\Windows\System` | `UserPolicyMode` | DWORD | `1` | **1 = Merge** (les paramètres Utilisateur de la GPO s'ajoutent à ceux de l'utilisateur) ; `2 = Replace` |

**Effet :** lorsque l'utilisateur `henry`/`jaona`/`tanjona`/`ventso` ouvre une session sur un poste client, ce poste applique l'intégralité de la **Configuration Ordinateur** de la GPO (audit, blocage web, USB, autorun…), même si ces paramètres ne concernent pas directement le compte utilisateur.

Implémentation (`GpoTemplateDeploymentService.cs`, `WithLoopbackMerge`) : idempotente (jamais dupliquée si le modèle contient déjà `UserPolicyMode`).

### 2.2 Filtrage de sécurité AD DS (Security Filtering)

La commande `SetGpoTargetingCommand` génère un script PowerShell qui :

1. Crée (si besoin) un groupe de sécurité AD DS dédié (`ADOps-<modèle>-<hash>`) dans `CN=Users`.
2. Synchronise ses membres avec les comptes ciblés.
3. Rétrograde `Authenticated Users` à **GpoRead** uniquement (lecture, pas d'application).
4. Accorde **GpoRead** au groupe **Domain Computers** (SID résolu par RID 515, valable sur un contrôleur de domaine localisé) — **indispensable** : sans ce droit, le poste client ne peut pas télécharger la stratégie.
5. Pour une GPO de portée Ordinateur (`ApplyToComputers = true`), accorde en plus **GpoApply** à **Domain Computers** : la configuration ordinateur s'applique réellement sur tous les postes clients du domaine.
6. Accorde `GpoEditDeleteModifySecurity` aux Domain Admins (édition uniquement, jamais `GpoApply`).
7. Accorde `GpoRead` au groupe de filtrage (utilisateurs ciblés) — nécessaire au traitement en boucle.

Le groupe cible reçoit `GpoApply` pour une portée Utilisateur, ou `GpoRead` pour une portée Ordinateur (l'application machine est portée par `Domain Computers`).

### 2.3 Ciblage hybride (Utilisateurs + Postes du domaine)

Pour une GPO de portée Ordinateur, `DeployAsync` :

1. Énumère les **postes clients** du domaine `sarytany.local` (`GetClientComputersAsync`) : comptes ordinateur activés, exclut les contrôleurs de domaine, les serveurs et l'OU `Domain Controllers`.
2. Lie la GPO aux **OU des postes clients** découverts (filtre `OU=`, car `New-GPLink` ne peut pas lier un conteneur `CN=`).
3. Si aucune OU identifiable, replie sur la **racine du domaine** (lien valide), avec filtrage par `Domain Computers`.
4. Les utilisateurs ciblés (`henry`, `jaona`, `tanjona`, `ventso`) sont ajoutés au groupe de filtrage (lecture) pour le loopback.
5. Le résultat expose la liste des GPO matérialisées, liées et le groupe de filtrage utilisé ; le processus aboutit à **Succès (code de sortie 0)** dès qu'au moins une GPO a été matérialisée et liée.


---

## 3. SCRIPT POWERSHELL CORRIGÉ (création / liaison sans erreur)

Le script ci-dessous est celui généré par `SetGpoTargetingCommand` pour une GPO de portée Ordinateur. Il est idempotent, utilise les SID (RID 515 / 512) pour rester valide sur un contrôleur de domaine localisé, et emploie `-Replace` pour ne jamais laisser subsister un `GpoApply` indésirable.

```powershell
# Prérequis : exécuté sur un contrôleur de domaine (ou poste avec RSAT),
# dans le contexte d'un compte disposant des droits sur les GPO du domaine.
Import-Module ActiveDirectory -ErrorAction Stop
Import-Module GroupPolicy -ErrorAction Stop

$gpo = Get-GPO -Guid '11111111-2222-3333-4444-555555555555' -ErrorAction Stop

# --- 1. Groupe de ciblage AD DS (créé dans CN=Users s'il n'existe pas) ---
$group = Get-ADGroup -Identity 'ADOps-Computer-0001' -ErrorAction SilentlyContinue
if ($null -eq $group) {
    $domain = Get-ADDomain -ErrorAction Stop
    $group = New-ADGroup -Name 'ADOps-Computer-0001' -SamAccountName 'ADOps-Computer-0001' `
        -GroupScope Global -GroupCategory Security -Path ('CN=Users,' + $domain.DistinguishedName) `
        -Description 'ADOps — ciblage de « … »' -PassThru -ErrorAction Stop
}

# Membres voulus (utilisateurs ciblés : lecture pour le loopback)
$desired = @(
    'CN=Henry,OU=Utilisateurs,DC=sarytany,DC=local',
    'CN=Jaona,OU=Utilisateurs,DC=sarytany,DC=local',
    'CN=Tanjona,OU=Utilisateurs,DC=sarytany,DC=local',
    'CN=Ventso,OU=Utilisateurs,DC=sarytany,DC=local'
)
$current = @(Get-ADGroupMember -Identity $group -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DistinguishedName)
foreach ($dn in $current) { if ($desired -notcontains $dn) { Remove-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop } }
$current = @(Get-ADGroupMember -Identity $group -ErrorAction SilentlyContinue | Select-Object -ExpandProperty DistinguishedName)
foreach ($dn in $desired) { if ($current -notcontains $dn) { Add-ADGroupMember -Identity $group -Members $dn -Confirm:$false -ErrorAction Stop } }

# --- 2. Authenticated Users : lecture seulement (jamais d'application par défaut) ---
$authUsers = Get-GPPermission -Guid $gpo.Id -All -ErrorAction SilentlyContinue | Where-Object { $_.Trustee.Sid.Value -eq 'S-1-5-11' } | Select-Object -First 1
if ($null -ne $authUsers) {
    Set-GPPermission -Guid $gpo.Id -TargetName $authUsers.Trustee.Name -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue
} else {
    Set-GPPermission -Guid $gpo.Id -TargetName 'Authenticated Users' -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue
}

# --- 3. Domain Computers : lecture obligatoire + GpoApply (portée Ordinateur) ---
$domainComputers = $null
try {
    $domainSid = (Get-ADDomain -ErrorAction Stop).DomainSID.Value
    $domainComputers = Get-ADGroup -Identity ($domainSid + '-515') -ErrorAction Stop
} catch {
    $domainComputers = Get-ADGroup -Filter "Name -eq 'Domain Computers' -or SamAccountName -eq 'Domain Computers'" -ErrorAction SilentlyContinue | Select-Object -First 1
}
if ($null -ne $domainComputers) {
    Set-GPPermission -Guid $gpo.Id -TargetName $domainComputers.Name -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction SilentlyContinue
    Set-GPPermission -Guid $gpo.Id -TargetName $domainComputers.Name -TargetType Group -PermissionLevel GpoApply -Replace -ErrorAction Stop
} else {
    Write-Warning 'Groupe Domain Computers introuvable : le filtrage ordinateur peut être incomplet.'
}

# --- 4. Domain Admins : édition seulement, jamais GpoApply ---
$domainAdmins = $null
try {
    $domainAdmins = Get-ADGroup -Identity ((Get-ADDomain -ErrorAction Stop).DomainSID.Value + '-512') -ErrorAction Stop
} catch { }
if ($null -ne $domainAdmins) {
    Set-GPPermission -Guid $gpo.Id -TargetName $domainAdmins.Name -TargetType Group -PermissionLevel GpoEditDeleteModifySecurity -Replace -ErrorAction SilentlyContinue
}

# --- 5. Groupe de filtrage cible : lecture (portée Ordinateur) ---
Set-GPPermission -Guid $gpo.Id -TargetName 'ADOps-Computer-0001' -TargetType Group -PermissionLevel GpoRead -Replace -ErrorAction Stop

$permissions = @(Get-GPPermission -Guid $gpo.Id -All | Select-Object Trustee, Permission, Inherited)
[PSCustomObject]@{
    GroupName      = $group.Name
    GroupDn        = $group.DistinguishedName
    Members        = $desired.Count
    DomainComputers= $domainComputers.Name
    ApplyToComputers = $true
    Permissions    = $permissions
}

# --- 6. Liaison de la GPO aux OU des postes clients (exemple) ---
New-GPLink -Name $gpo.DisplayName -Target 'OU=Postes_Clients,DC=sarytany,DC=local' -LinkEnabled Yes -ErrorAction Stop
# Si plusieurs OU : répéter New-GPLink pour chaque OU. Si aucune OU, lier à la racine du domaine :
# New-GPLink -Name $gpo.DisplayName -Target 'DC=sarytany,DC=local' -LinkEnabled Yes
```

**Points de vigilance :**
- `-Replace` est indispensable : sans lui, `Set-GPPermission` ne rétrograde jamais un droit existant (`GpoApply` resterait appliqué à `Authenticated Users`).
- Le lien GPO se fait toujours sur une **OU** (jamais sur `CN=Users` / `CN=Computers` qui sont des conteneurs, non des OU).

---

## 4. FICHIERS MODIFIÉS / CRÉÉS

| Fichier | Action | Description |
|---|---|---|
| `backend/ADOps.GpoManager.ActiveDirectory/Services/GpoTemplateDeploymentService.cs` | Modifié | `WithLoopbackMerge` (UserPolicyMode = 1 = Merge) appliqué aux GPO Ordinateur ; ciblage hybride via `GetClientComputersAsync` ; exécuteur cohérent (Local/Distant) pour l'énumération des OU (correction ligne 66). |
| `backend/ADOps.GpoManager.Core/Commands/SetGpoTargetingCommand.cs` | Modifié | `Domain Computers` reçoit toujours `GpoRead` ; `GpoApply` ajouté pour la portée Ordinateur ; `Authenticated Users` rétrogradé à `GpoRead` avec `-Replace`. |
| `backend/ADOps.GpoManager.Core/Models/TemplateDeploymentModels.cs` | Modifié | `TemplateDeploymentResult` expose `Records`, `RefreshedComputers`, `FailedComputers` et `SummaryText` (liste des GPO matérialisées/liées). |
| `frontend/ADOps.GpoManager.App/ViewModels/GpoAssistantViewModel.cs` | Modifié | Appelle `DeployAsync` et affiche la liste des GPO matérialisées et liées (`Records`). |
| `README.md` | Modifié | "Limites connues" : loopback processing (Merge) = oui pour les GPO Ordinateur. |
| `RESOLUTION_LOOPBACK_PROCESSING.md` | Créé | Ce document. |
| `tests/ADOps.GpoManager.Tests/GpoTargetingScriptTests.cs` | Créé | Tests unitaires : filtrage `Domain Computers`, `GpoApply` portée Ordinateur, idempotence du loopback Merge. |

---

## 5. PROCÉDURE DE VALIDATION SUR LE POSTE CLIENT

Exécuter ces commandes sur un poste client du domaine `sarytany.local` (ouvrir une session avec l'un des utilisateurs ciblés : `henry`, `jaona`, `tanjona`, `ventso`) :

### 5.1 Actualisation forcée des stratégies

```cmd
gpupdate /force
```

Attendre la fin de l'actualisation (ordinateur et utilisateur). Le code de retour doit être `0`.

### 5.2 Vérification globale (Configuration Ordinateur)

```cmd
gpresult /r /scope computer
```

**Critère de succès :** la GPO déployée par ADOps (ex. `ADOps — …`) apparaît dans la liste des **stratégies de groupe appliquées** (Applied Group Policy Objects), côté **Configuration Ordinateur**, et non sous "Refusés".

### 5.3 Rapport détaillé (optionnel)

```cmd
gpresult /h C:\Rapport-GPO.html /f
```
Puis ouvrir `C:\Rapport-GPO.html` et vérifier que les paramètres Ordinateur (Audit, Blocage Web, USB, Autorun…) sont bien appliqués.

---

## 6. RÉSULTAT ATTENDU

- Le déploiement se termine avec un **code de sortie 0 (Succès)**.
- L'interface affiche la liste des **GPO matérialisées et liées** (OU cibles + groupe de filtrage).
- Sur le poste client, après `gpupdate /force`, `gpresult /r /scope computer` affiche la GPO comme appliquée.
- Les paramètres Configuration Ordinateur (Audit, Blocage Web, USB, Autorun…) s'appliquent sur le poste dès qu'un utilisateur ciblé s'y connecte, grâce au **Loopback Processing en mode Merge**.

- Le groupe `Domain Computers` doit garder `GpoRead` ; sans lui, le client ne télécharge pas la stratégie.
