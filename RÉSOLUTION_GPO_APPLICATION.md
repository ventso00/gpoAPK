# RÉSOLUTION DE L'ÉCHEC D'APPLICATION DES GPO ACTIVE DIRECTORY

> **Projet :** ADOps GPO Manager — WPF / MVVM / .NET 10  
> **Utilisateur cible :** `Tanjona` (et tout utilisateur client)  
> **Objectif :** Faire en sorte que les stratégies de groupe (GPO) s'appliquent réellement aux postes clients, avec validation par `gpresult` et test fonctionnel (blocage de `cmd.exe`).

---

## 1. PROBLÈMES IDENTIFIÉS

### 1.1 L'utilisateur se trouve dans le conteneur par défaut `CN=Users`

**Symptôme :** La GPO est créée et liée, mais n'apparaît jamais dans `gpresult /r` pour l'utilisateur.

**Cause racine :** Dans Active Directory, les GPO **ne peuvent être liées qu'aux OU (Unités d'Organisation)** et au domaine. Elles **ne peuvent pas être liées aux conteneurs par défaut** comme `CN=Users` ou `CN=Computers`. Si `Tanjona` est dans `CN=Users`, aucune GPO liée à une OU ne lui s'applique.

**Preuve dans le code :** La classe `LinkGpoCommand` valide que la cible est un DN LDAP valide. De plus, la documentation du projet précise : *"Les modèles de portée Utilisateur ne s'appliquent qu'aux comptes utilisateurs des OU liées"*.

### 1.2 La GPO n'est pas liée à l'OU où réside l'utilisateur

**Symptôme :** `gpresult /r` affiche la GPO sous "Refusés" ou ne l'affiche pas du tout.

**Cause racine :** La GPO `Blocage CMD et Registre` (`cmd-registry-block`) doit être liée directement à l'OU contenant `Tanjona`. Si elle est liée à une autre OU ou à la racine du domaine uniquement, l'héritage peut ne pas fonctionner correctement.

### 1.3 Filtrage de sécurité manquant ou incorrect

**Symptôme :** La GPO apparaît dans `gpresult` sous "Refusés" (Denied).

**Cause racine :** Par défaut, une nouvelle GPO accorde la lecture (`Read`) à `Authenticated Users` mais pas forcément l'application (`Apply Group Policy`). Si `SetGpoTargetingCommand` crée un groupe de sécurité ciblé mais que `Tanjona` n'en est pas membre, la GPO est filtrée.

**Comportement du code :** `SetGpoTargetingCommand` définit :
- `Authenticated Users` → `None` puis `GpoRead` (lecture seulement)
- Groupe cible → `GpoApply` (application autorisée)

Si `Tanjona` n'est pas dans le groupe cible, il ne reçoit pas la GPO.

### 1.4 Héritage des GPO bloqué sur l'OU

**Symptôme :** La GPO liée à l'OU parente n'est pas appliquée.

**Cause racine :** L'option "Bloquer l'héritage" (Block Inheritance) est activée sur l'OU cible. Cela empêche toutes les GPO liées aux OU parentes de s'appliquer aux objets de cette OU.

### 1.5 `gpupdate /force` n'a pas été exécuté

**Symptôme :** La GPO est visible dans AD mais pas encore effective sur le poste client.

**Cause racine :** Le rafraîchissement des stratégies utilisateur se fait par défaut toutes les 90 minutes (plus un décalage aléatoire de 0 à 30 minutes). Sans `gpupdate /force`, le poste client n'a pas encore récupéré la nouvelle stratégie.

### 1.6 Dépendance inutile à WinRM (CORRIGÉ)

**Problème identifié :** Le code original appelait `GetAllOusAsync(_executors.Local, ...)` même en mode distant (WinRM), ce qui créait une dépendance incohérente et forçait l'installation de RSAT sur la machine d'administration.

**Correction apportée :** La ligne 64 de `GpoTemplateDeploymentService.cs` utilise maintenant la variable `executor` (déjà déterminée selon `ExecutorMode`) au lieu de forcer `_executors.Local`. Ainsi :
- **Mode local (RSAT) :** Pas de WinRM nécessaire, exécution via modules AD/GroupPolicy locaux.
- **Mode distant (WinRM) :** WinRM utilisé uniquement pour se connecter au contrôleur de domaine, pas aux postes clients.

**Ce qui ne dépend PAS de WinRM (opérations AD côté serveur) :**
- ✅ Création de GPO (`New-GPO`)
- ✅ Configuration de registre dans la GPO (`Set-GPRegistryValue`)
- ✅ Liaison de GPO à une OU (`New-GPLink`)
- ✅ Filtrage de sécurité (`Set-GPPermission`)
- ✅ Déplacement d'utilisateur vers une OU (`Move-ADObject`)
- ✅ Lecture de l'inventaire AD (`Get-ADUser`, `Get-ADOrganizationalUnit`)

**Ce qui dépend de WinRM (opérations côté poste client) :**
- ⚠️ Rafraîchissement forcé (`gpupdate /force`) — nécessite WinRM sur le client
- ⚠️ Vérification (`gpresult /r`) — nécessite WinRM sur le client
- ⚠️ Redémarrage/extinction client — nécessite WinRM sur le client

---

## 2. MODIFICATIONS APPORTÉES AU CODE

### 2.1 Nouveau fichier : `MoveAdUserCommand.cs`

**Chemin :** `backend/ADOps.GpoManager.Core/Commands/MoveAdUserCommand.cs`

Commande PowerShell typée qui déplace un utilisateur vers une OU via `Move-ADObject` + `Get-ADUser`.

**Fonctionnalités :**
- Valide les DistinguishedName (source et cible) via `DistinguishedNameValidator`
- Rejette les cibles qui ne sont pas des OU (`OU=` requis)
- Détecte si l'utilisateur est déjà dans l'OU cible
- Retourne un objet structuré (Nom, SamAccountName, PreviousDn, TargetOu, Timestamp)

**Aucune dépendance WinRM sur le poste client** — s'exécute sur le contrôleur de domaine via RSAT.

### 2.2 Modification : `IDirectoryService.cs`

**Chemin :** `backend/ADOps.GpoManager.Core/Services/IDirectoryService.cs`

Ajout de la méthode `MoveToOuAsync` à l'interface, permettant le déplacement d'un utilisateur vers une OU depuis le service d'annuaire.

### 2.3 Modification : `DirectoryService.cs`

**Chemin :** `backend/ADOps.GpoManager.ActiveDirectory/Services/DirectoryService.cs`

Implémentation de `MoveToOuAsync` qui :
- Valide les entrées (utilisateur et OU cible non vides)
- Sélectionne l'exécuteur selon `ExecutorMode` (local ou distant)
- Exécute `MoveAdUserCommand`
- Audit l'opération (succès/échec/annulation) via `AuditActions.DirectoryWrite`
- Gère les exceptions et l'annulation proprement

### 2.4 Modification : `AuditEntry.cs`

**Chemin :** `backend/ADOps.GpoManager.Core/Models/AuditEntry.cs`

Ajout de la constante `AuditActions.DirectoryWrite = "DirectoryWrite"` pour l'audit des opérations d'écriture sur l'annuaire.

### 2.5 Correction de bug : `GpoTemplateDeploymentService.cs`

**Chemin :** `backend/ADOps.GpoManager.ActiveDirectory/Services/GpoTemplateDeploymentService.cs`

**Ligne 64 (avant) :**
```csharp
var allOus = await GetAllOusAsync(_executors.Local, cancellationToken).ConfigureAwait(false);
```

**Ligne 64 (après) :**
```csharp
// Utilise le même exécuteur (local ou distant selon le mode) pour cohérence.
var allOus = await GetAllOusAsync(executor, cancellationToken).ConfigureAwait(false);
```

**Impact :** En mode distant (WinRM vers le DC), la liste des OUs est maintenant récupérée via le même exécuteur distant, évitant une dépendance incohérente à RSAT local.

---

## 3. PROCÉDURE DE RÉSOLUTION — ÉTAPE PAR ÉTAPE

### ÉTAPE 1 : Déplacer l'utilisateur dans une OU dédiée

#### 1.1 Créer l'OU `Utilisateurs_Clients`

Sur le contrôleur de domaine, PowerShell en tant qu'administrateur :

```powershell
Import-Module ActiveDirectory

# Créer l'OU si elle n'existe pas
if (-not (Get-ADOrganizationalUnit -Filter "Name -eq 'Utilisateurs_Clients'" -ErrorAction SilentlyContinue)) {
    New-ADOrganizationalUnit -Name "Utilisateurs_Clients" -Path "DC=domaine,DC=local"
    Write-Host "OU Utilisateurs_Clients créée." -ForegroundColor Green
} else {
    Write-Host "OU Utilisateurs_Clients existe déjà." -ForegroundColor Yellow
}
```

#### 1.2 Déplacer `Tanjona` de `CN=Users` vers l'OU

```powershell
$utilisateur = Get-ADUser -Identity "Tanjona"
$ou = Get-ADOrganizationalUnit -Identity "OU=Utilisateurs_Clients,DC=domaine,DC=local"

if ($utilisateur.DistinguishedName -ne ("CN=" + $utilisateur.SamAccountName + "," + $ou.DistinguishedName)) {
    Move-ADObject -Identity $utilisateur.DistinguishedName -TargetPath $ou.DistinguishedName
    Write-Host "Tanjona déplacé vers $($ou.DistinguishedName)" -ForegroundColor Green
} else {
    Write-Host "Tanjona est déjà dans l'OU cible." -ForegroundColor Yellow
}
```

#### 1.3 Vérifier le déplacement

```powershell
Get-ADUser -Identity "Tanjona" -Properties DistinguishedName
# Résultat attendu : CN=Tanjona,OU=Utilisateurs_Clients,DC=domaine,DC=local
```

**Via ADOps GPO Manager (après les modifications apportées) :**
Utiliser `IDirectoryService.MoveToOuAsync(context, dnUtilisateur, dnOuCible)`.

---

### ÉTAPE 2 : Lier la GPO à l'OU cible

#### 2.1 Vérifier les liens existants

```powershell
Import-Module GroupPolicy
Get-GPInheritance -Target "OU=Utilisateurs_Clients,DC=domaine,DC=local"
```

#### 2.2 Lier la GPO `Blocage CMD et Registre`

```powershell
New-GPLink -Name "Blocage CMD et Registre" -Target "OU=Utilisateurs_Clients,DC=domaine,DC=local" -LinkEnabled Yes
```

**Via ADOps GPO Manager :**
Page "Déploiements" → Sélectionner la GPO `cmd-registry-block` → Cocher l'OU `OU=Utilisateurs_Clients` → Valider le plan d'impact → Déployer.

#### 2.3 Vérifier que le lien est activé

```powershell
(Get-GPInheritance -Target "OU=Utilisateurs_Clients,DC=domaine,DC=local").GpoLinks |
    Where-Object { $_.DisplayName -eq "Blocage CMD et Registre" } |
    Select-Object DisplayName, Enabled, Order
```

---

### ÉTAPE 3 : Configurer le filtrage de sécurité

#### 3.1 Vérifier les permissions actuelles

```powershell
Get-GPPermission -Name "Blocage CMD et Registre" -All | Select-Object Trustee, Permission
```

#### 3.2 Donner les droits à `Authenticated Users`

```powershell
Set-GPPermission -Name "Blocage CMD et Registre" -TargetName "Authenticated Users" -TargetType Group -PermissionLevel GpoRead
Set-GPPermission -Name "Blocage CMD et Registre" -TargetName "Authenticated Users" -TargetType Group -PermissionLevel GpoApply
```

#### 3.3 Ajouter explicitement `Tanjona` (optionnel, si pas de groupe ciblé)

```powershell
Set-GPPermission -Name "Blocage CMD et Registre" -TargetName "Tanjona" -TargetType User -PermissionLevel GpoApply
```

#### 3.4 Si un groupe de sécurité ciblé est utilisé (via ADOps)

ADOps crée un groupe `ADOps-<slug>-XXXX`. Vérifier que `Tanjona` en est membre :

```powershell
$groupe = Get-ADGroup -Filter "Name -like 'ADOps-cmd-registry-block*'" | Select-Object -First 1
if ($groupe) {
    Get-ADGroupMember -Identity $groupe.DistinguishedName | Select-Object Name, SamAccountName
}
```

**Important :** Les administrateurs de domaine sont exclus par construction par `SetGpoTargetingCommand`. Si `Tanjona` est admin, utiliser un compte utilisateur standard pour le test.

---

### ÉTAPE 4 : Vérifier que l'héritage n'est pas bloqué

Via `dsa.msc` (Utilisateurs et ordinateurs Active Directory) :
1. Affichage → Fonctionnalités avancées
2. Clic droit sur l'OU `Utilisateurs_Clients` → Propriétés
3. Onglet Objet → Décocher "Bloquer l'héritage" si coché

---

### ÉTAPE 5 : Activer WinRM sur les postes clients (UNIQUEMENT pour gpupdate/gpresult à distance)

**Sur chaque poste client (PC Client 1, PC Client 2), PowerShell en tant qu'administrateur :**

```powershell
Enable-PSRemoting -Force
```

**Vérifier :**
```powershell
Get-Service WinRM | Select-Object Name, Status, StartType
# Doit afficher : Running, Automatic
```

**Note :** Les opérations AD côté serveur (création GPO, lien, filtrage, déplacement utilisateur) ne nécessitent **pas** WinRM sur les postes clients.

---

### ÉTAPE 6 : Forcer le rafraîchissement des stratégies

**Sur le poste client, sous la session de `Tanjona` :**

```cmd
gpupdate /force /target:user
```

**Via ADOps (à distance via WinRM) :**
`InvokeGpUpdateCommand` exécute `gpupdate.exe /force /target:user` sur le poste cible.

---

### ÉTAPE 7 : Valider l'application de la GPO

#### 7.1 Rapport `gpresult`

Sur le poste client, sous la session `Tanjona` :

```cmd
gpresult /r
```

Ou rapport HTML détaillé :
```cmd
gpresult /h C:\rapport_gpo.html
```

**Ce qu'il faut voir :**
- Section **"Objets Stratégie de groupe appliqués" (Applied GPOs)** : `Blocage CMD et Registre`
- **PAS** sous "Refusés" (Denied) ou "Filtrés" (Filtered)

#### 7.2 Test fonctionnel : blocage de `cmd.exe`

Ouvrir une invite de commandes (`cmd.exe`) sous la session de `Tanjona`.

**Critère de succès absolu :** Le système affiche :
> *"L'invite de commandes a été désactivée par votre administrateur."*

---

---

## 4. CHECKLIST DE VALIDATION FINALE

| # | Vérification | Statut |
|---|-------------|--------|
| 1 | `Tanjona` est dans `OU=Utilisateurs_Clients` (pas `CN=Users`) | ☐ |
| 2 | GPO `Blocage CMD et Registre` liée à `OU=Utilisateurs_Clients` | ☐ |
| 3 | Lien GPO activé (`LinkEnabled = Yes`) | ☐ |
| 4 | `Authenticated Users` a Read + Apply sur la GPO | ☐ |
| 5 | `Tanjona` membre du groupe de filtrage (si utilisé) | ☐ |
| 6 | Héritage non bloqué sur l'OU | ☐ |
| 7 | WinRM activé sur les postes clients (pour gpupdate/gpresult distant) | ☐ |
| 8 | `gpupdate /force` exécuté sous la session Tanjona | ☐ |
| 9 | `gpresult /r` affiche la GPO sous "Applied GPOs" | ☐ |
| 10 | `cmd.exe` affiche le message de blocage administrateur | ☐ |

---

## 5. ARCHITECTURE — DÉPENDANCES WINRM

| Opération | Exécuté sur | WinRM client requis ? | Module requis |
|-----------|-------------|----------------------|---------------|
| Créer GPO (`New-GPO`) | Contrôleur de domaine | Non | GroupPolicy |
| Lier GPO à OU (`New-GPLink`) | Contrôleur de domaine | Non | GroupPolicy |
| Filtrage sécurité (`Set-GPPermission`) | Contrôleur de domaine | Non | GroupPolicy |
| Déplacer utilisateur (`Move-ADObject`) | Contrôleur de domaine | Non | ActiveDirectory |
| Lire inventaire AD (`Get-ADUser`, etc.) | Contrôleur de domaine | Non | ActiveDirectory |
| `gpupdate /force` sur client | Poste client | **Oui** | — |
| `gpresult /r` sur client | Poste client | **Oui** | — |
| Redémarrer/éteindre client | Poste client | **Oui** | — |

**Conclusion :** L'application ADOps peut créer, lier et filtrer des GPO sans aucune dépendance WinRM sur les postes clients. WinRM est uniquement nécessaire pour la vérification distante et les actions de puissance.

---

## 6. FICHIERS MODIFIÉS / CRÉÉS

| Fichier | Action | Description |
|---------|--------|-------------|
| `backend/ADOps.GpoManager.Core/Commands/MoveAdUserCommand.cs` | **Créé** | Commande PowerShell pour déplacer un utilisateur vers une OU |
| `backend/ADOps.GpoManager.Core/Services/IDirectoryService.cs` | **Modifié** | Ajout de `MoveToOuAsync` à l'interface |
| `backend/ADOps.GpoManager.ActiveDirectory/Services/DirectoryService.cs` | **Modifié** | Implémentation de `MoveToOuAsync` |
| `backend/ADOps.GpoManager.Core/Models/AuditEntry.cs` | **Modifié** | Ajout de `AuditActions.DirectoryWrite` |
| `backend/ADOps.GpoManager.ActiveDirectory/Services/GpoTemplateDeploymentService.cs` | **Modifié** | Correction bug : utilisation de l'exécuteur cohérent (ligne 64) |
| `RÉSOLUTION_GPO_APPLICATION.md` | **Créé** | Cette documentation |

---

## 7. COMMANDES DE RÉPARATION RAPIDE (tout d'un coup)

### Sur le contrôleur de domaine :

```powershell
Import-Module ActiveDirectory, GroupPolicy

# 1. Créer l'OU
New-ADOrganizationalUnit -Name "Utilisateurs_Clients" -Path "DC=domaine,DC=local" -ErrorAction SilentlyContinue

# 2. Déplacer Tanjona
$u = Get-ADUser "Tanjona"
$ou = Get-ADOrganizationalUnit "OU=Utilisateurs_Clients,DC=domaine,DC=local"
Move-ADObject -Identity $u.DistinguishedName -TargetPath $ou.DistinguishedName

# 3. Créer/Configurer la GPO
$gpo = New-GPO -Name "Blocage CMD et Registre" -Comment "Désactive cmd.exe pour utilisateurs standards" -ErrorAction SilentlyContinue
Set-GPRegistryValue -Name $gpo.DisplayName -Key "HKCU\Software\Policies\Microsoft\Windows\System" -ValueName "DisableCMD" -Type DWord -Value 2 -ErrorAction SilentlyContinue

# 4. Lier la GPO à l'OU
New-GPLink -Name $gpo.DisplayName -Target "OU=Utilisateurs_Clients,DC=domaine,DC=local" -LinkEnabled Yes -ErrorAction SilentlyContinue

# 5. Filtrage de sécurité
Set-GPPermission -Name $gpo.DisplayName -TargetName "Authenticated Users" -TargetType Group -PermissionLevel GpoRead -ErrorAction SilentlyContinue
Set-GPPermission -Name $gpo.DisplayName -TargetName "Authenticated Users" -TargetType Group -PermissionLevel GpoApply -ErrorAction SilentlyContinue
```

### Sur chaque poste client :

```powershell
Enable-PSRemoting -Force
```

### Sous la session de `Tanjona` sur le poste client :

```cmd
gpupdate /force
gpresult /r
```

Puis tester `cmd.exe` → doit être bloqué avec le message administrateur.

---

> **Note :** Ce document couvre la résolution complète de l'échec d'application des GPO. Aucune dépendance WinRM n'est requise sur les postes clients pour les opérations côté Active Directory (création, lien, filtrage, déplacement utilisateur). WinRM est uniquement nécessaire pour le rafraîchissement distant (`gpupdate`) et la vérification (`gpresult`) sur les postes clients.