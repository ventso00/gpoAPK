# Catalogue GPO — les 16 modèles déployables sur les postes

Ce document est la référence opposable du catalogue embarqué dans l'exécutable.
Il décrit les 16 modèles livrés, leurs paramètres, les valeurs de registre
réellement écrites dans la GPO, leurs prérequis et leurs limites connues.

> **État vérifié dans le code** : les 16 modèles possèdent tous une liaison de
> déploiement (`TemplateRegistryBindings`) et sont matérialisés par
> `TemplateCatalog.Materialize()`. Le test `TemplateMaterializationTests`
> vérifie qu'aucun modèle ne produit une GPO vide. Le catalogue est donc
> entièrement utilisable sur un domaine réel ; aucun modèle n'est une simple
> fiche documentaire.

---

## 1. Chaîne de déploiement réelle

Pour chaque modèle sélectionné dans la page **« GPO à appliquer »** :

1. **Matérialisation** — les paramètres saisis sont fusionnés avec les valeurs
   par défaut, puis convertis en écritures de registre
   (`HKCU\…` ou `HKLM\…`). Un jeu de paramètres qui ne produit aucune écriture
   est refusé (pas de GPO vide).
2. **Création de la GPO** — `New-GPO` (ou réutilisation de la GPO portant le
   même nom), puis nettoyage ciblé des anciennes valeurs et
   `Set-GPRegistryValue` pour chaque écriture. Les listes indexées
   (`URLBlocklist`, règles ASR) sont purgées avant réécriture.
3. **Filtrage AD DS optionnel** — création d'un groupe `ADOps-<modèle>-<hash>`
   et `Set-GPPermission` sur la GPO. Les administrateurs sont exclus par
   construction ; le groupe ne contient que les postes (ou comptes standards)
   sélectionnés.
4. **Liaison** — `New-GPLink` sur chaque OU cible réelle :
   - portée **Ordinateur** : OUs des postes sélectionnés ;
   - portée **Utilisateur** : OUs des comptes standards ciblés, avec repli sur
     les OUs des postes si nécessaire.
   La liaison n'est créée qu'après le filtrage, afin de ne jamais appliquer la
   GPO à toute l'OU par erreur.
5. **Actualisation et validation** — `gpupdate /force` puis `gpresult /r` via
   WinRM sur les postes sélectionnés, avec résultat par poste dans la console.
6. **Traçabilité** — chaque étape est écrite dans le journal d'audit SQLite
   (`audit.db`) et dans `logs\adops.log`, avec l'identifiant de la GPO.

**Cibles et portées** : 12 modèles sont de portée **Ordinateur** (HKLM) et
4 de portée **Utilisateur** (HKCU). Tous sont applicables aux machines
Windows 10/11 ciblées ; les modèles Utilisateur nécessitent au moins un compte
standard dans les départements des postes sélectionnés (sinon le déploiement
refuse la GPO avec un message explicite).

---

## 2. Vue d'ensemble des 16 modèles

| # | Identifiant | Nom | Catégorie | Portée | Risque | Paramètres |
|---|---|---|---|---|---|---|
| 1 | `asr-exploit-guard` | Réduction de la surface d'attaque (Exploit Guard — ASR) | Sécurité | Ordinateur | Moyen | `ruleIds` |
| 2 | `audit-policy-subcategory` | Audit avancé — forçage des sous-catégories | Audit | Ordinateur | Faible | `enableSubcategoryOverride` |
| 3 | `autorun-hardening` | Désactivation de l'exécution automatique | Périphériques | Ordinateur | Faible | `disableAutorun` |
| 4 | `browser-url-blocklist` | Blocage et autorisation de sites Web (Edge / Chrome) | Internet | Ordinateur | Faible | `browser`, `blockedUrls`, `allowedUrls` |
| 5 | `cmd-registry-block` | Blocage de l'invite de commandes et de l'Éditeur du Registre | Restrictions | Utilisateur | Moyen | `cmdMode`, `disableRegistryTools` |
| 6 | `control-panel-settings` | Blocage du Panneau de configuration et des Paramètres | Restrictions | Utilisateur | Faible | `blockControlPanel` |
| 7 | `legacy-protocols-hardening` | Désactivation des protocoles hérités (SMBv1, LLMNR, NTLMv1) | Réseau | Ordinateur | Élevé | `disableSmb1`, `disableLlmnr`, `enforceNtlmv2` |
| 8 | `powershell-execution-policy` | Politique d'exécution des scripts PowerShell | Restrictions | Ordinateur | Élevé | `executionPolicy` |
| 9 | `rdp-lockdown` | Verrouillage du Bureau à distance (RDP) | Réseau | Ordinateur | Moyen | `mode` |
| 10 | `removable-storage-restrictions` | Restrictions de stockage amovible (USB) | Périphériques | Ordinateur | Moyen | `access` |
| 11 | `screensaver-lock` | Verrouillage automatique par écran de veille | Sécurité | Utilisateur | Faible | `timeoutSeconds` |
| 12 | `shell-restrictions` | Restrictions de l'interface Windows (shell) | Restrictions | Utilisateur | Moyen | `disableRunDialog`, `disableWinKeys`, `hidePowerCommands`, `disableLockWorkstation` |
| 13 | `smartscreen-enforcement` | Filtrage SmartScreen de Windows | Sécurité | Ordinateur | Faible | `level` |
| 14 | `uac-hardening` | Renforcement du contrôle de compte d'utilisateur (UAC) | Sécurité | Ordinateur | Moyen | `secureDesktop`, `standardUserBehavior` |
| 15 | `windows-defender-firewall` | Windows Defender et pare-feu Windows | Sécurité | Ordinateur | Élevé | `firewallEnabled`, `realTimeProtectionEnabled` |
| 16 | `windows-update` | Windows Update (Windows Update ou WSUS) | Mise à jour | Ordinateur | Moyen | `mode`, `wuServerUrl`, `wuStatusServerUrl`, `auOptions` |

Les 16 modèles sont marqués `Available` dans leur fiche JSON (statut de support
vérifié). Les prérequis de chaque fiche sont repris ci-dessous.

---

## 3. Fiche détaillée et valeurs de registre écrites

### 3.1 `asr-exploit-guard` — ASR Defender

- **Paramètre** : `ruleIds` (multiligne, GUID par ligne, obligatoire).
  Valeur par défaut : 8 règles ASR courantes.
- **Écritures** :
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR`
    → `ExploitGuard_ASR_Rules` (DWORD) = `1` ;
  - `…\ASR\Rules\<GUID>` (REG_SZ) = `1` pour chaque règle listée.
- **Prérequis** : Microsoft Defender actif, Windows 10 1709+ pour l'ensemble
  des règles.
- **Risque** : moyen. Tester d'abord en audit (`=2`) sur un anneau pilote.
  Certaines règles (LSASS) gênent les outils de diagnostic légitimes.

### 3.2 `audit-policy-subcategory` — Audit avancé

- **Paramètre** : `enableSubcategoryOverride` (booléen, obligatoire).
- **Écriture** : `HKLM\SYSTEM\CurrentControlSet\Control\Lsa`
  → `SCENoApplyLegacyAuditPolicy` (DWORD) = `1`.
- **Prérequis** : définir les sous-catégories effectives par stratégie avancée
  ou `auditpol.exe` (voir `docs/03-SecurityHardening.md`).
- **Limite** : ce modèle force la prévalence des sous-catégories, il ne définit
  pas à lui seul les événements audités.

### 3.3 `autorun-hardening` — Autorun / AutoPlay

- **Paramètre** : `disableAutorun` (booléen, obligatoire).
- **Écritures** :
  - `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer`
    → `NoAutorun` (DWORD) = `1` ;
  - même clé → `NoDriveTypeAutoRun` (DWORD) = `255`.
- **Prérequis** : aucun (stratégies natives Explorer).
- **Limite** : ne remplace pas une restriction des supports amovibles ;
  certains médias OEM ne se lancent plus automatiquement.

### 3.4 `browser-url-blocklist` — Edge / Chrome

- **Paramètres** :
  - `browser` : `Edge`, `Chrome` ou `Les deux` ;
  - `blockedUrls` : une URL par ligne (obligatoire) ;
  - `allowedUrls` : exceptions, une par ligne (facultatif).
- **Écritures** (sous-clés de stratégie, valeurs indexées `1..N` en REG_SZ) :
  - `HKLM\SOFTWARE\Policies\Microsoft\Edge\URLBlocklist`
  - `HKLM\SOFTWARE\Policies\Microsoft\Edge\URLAllowlist`
  - `HKLM\SOFTWARE\Policies\Google\Chrome\URLBlocklist`
  - `HKLM\SOFTWARE\Policies\Google\Chrome\URLAllowlist`
- **Prérequis** : ADMX Edge (inclus) et ADMX Chrome (magasin central si absent).
- **Limite** : contrôle du navigateur visé uniquement ; un filtrage DNS/proxy
  reste nécessaire pour une couverture réseau complète.

### 3.5 `cmd-registry-block` — cmd / regedit

- **Paramètres** :
  - `cmdMode` : `Interdit (scripts inclus)` → `DisableCMD=2`, ou
    `Interdit (cmd seule)` → `DisableCMD=1` ;
  - `disableRegistryTools` : booléen → `DisableRegistryTools=1`.
- **Écritures** :
  - `HKCU\Software\Policies\Microsoft\Windows\System`
    → `DisableCMD` (DWORD) ;
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System`
    → `DisableRegistryTools` (DWORD).
- **Limite** : ne bloque ni `reg.exe` en ligne de commande ni PowerShell ;
  restriction de shell, pas une frontière de sécurité.

### 3.6 `control-panel-settings` — Panneau de configuration

- **Paramètre** : `blockControlPanel` (booléen, obligatoire).
- **Écriture** : `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer`
  → `NoControlPanel` (DWORD) = `1`.
- **Prérequis** : lier la GPO aux comptes standards ; les administrateurs
  doivent rester hors de portée (filtrage de sécurité).
- **Limite** : masque aussi les outils d'administration du Panneau de
  configuration pour les utilisateurs ciblés. Une valeur `false` ne produit
  aucune écriture : la GPO est refusée plutôt que créée vide.

### 3.7 `legacy-protocols-hardening` — SMBv1 / LLMNR / NTLMv1

- **Paramètres** : `disableSmb1`, `disableLlmnr`, `enforceNtlmv2` (booléens).
- **Écritures** :
  - `HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters`
    → `SMB1` (DWORD) = `0` ;
  - `HKLM\SYSTEM\CurrentControlSet\Services\mrxsmb10` → `Start` (DWORD) = `4` ;
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\DNSClient`
    → `EnableMulticast` (DWORD) = `0` ;
  - `HKLM\SYSTEM\CurrentControlSet\Control\Lsa`
    → `LmCompatibilityLevel` (DWORD) = `5`.
- **Prérequis** : aucun équipement métier ne dépend de SMBv1/NTLMv1.
  Inventorier avant déploiement (`Get-SmbConnection`, journaux SMBServer).
- **Risque** : élevé ; c'est le modèle le plus susceptible de couper des
  imprimantes/NAS anciens.

### 3.8 `powershell-execution-policy` — PowerShell

- **Paramètre** : `executionPolicy` : `Désactivé`, `AllSigned`,
  `RemoteSigned`, `Unrestricted`.
- **Écritures** :
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\PowerShell`
    → `EnableScripts` (DWORD) = `0` si Désactivé, sinon `1` ;
  - même clé → `ExecutionPolicy` (REG_SZ) = valeur choisie, sauf en mode
    Désactivé.
- **Prérequis** : inventorier les scripts métier non signés ; `AllSigned` exige
  une chaîne de signature de code opérationnelle.
- **Risque** : élevé ; la politique machine s'applique aussi aux comptes
  administrateur locaux.

### 3.9 `rdp-lockdown` — Bureau à distance

- **Paramètre** : `mode` : `Désactivé` ou `NLA requis`.
- **Écritures** :
  - `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server`
    → `fDenyTSConnections` (DWORD) = `1` (désactivé) ou `0` (NLA requis) ;
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows NT\Terminal Services`
    → `UserAuthentication` (DWORD) = `1` en mode NLA.
- **Prérequis** : aucun support RMM/RDP ne doit dépendre du mode désactivé ;
  clients RDP compatibles CredSSP pour NLA.
- **Risque** : moyen ; prévoir un canal d'assistance alternatif.

### 3.10 `removable-storage-restrictions` — USB

- **Paramètre** : `access` : `Accès complet`, `Lecture seule`, `Bloqué`.
- **Écritures** dans
  `HKLM\SOFTWARE\Policies\Microsoft\Windows\RemovableStorageDevices\{53f56307-b6bf-11d0-94f2-00a0c91efb8b}` :
  - Bloqué : `Deny_All=1` ; Lecture seule : `Deny_All=0`, `Deny_Write=1` ;
  - Accès complet : `Deny_All=0`, `Deny_Write=0` (remise à zéro explicite pour
    éviter qu'un ancien mode reste actif lors d'un redéploiement).
- **Prérequis** : aucun ADMX.
- **Limite** : concerne la classe « disques » (USB, SD, disques externes) ;
  un blocage total empêche aussi les sauvegardes sur support amovible.

### 3.11 `screensaver-lock` — Verrouillage de session

- **Paramètre** : `timeoutSeconds` (entier, obligatoire ; défaut 600).
- **Écritures** dans
  `HKCU\Software\Policies\Microsoft\Windows\Control Panel\Desktop` :
  - `ScreenSaveActive` (REG_SZ) = `1`, `ScreenSaverIsSecure` = `1`,
  - `ScreenSaveTimeOut` = valeur du paramètre,
  - `SCRNSAVE.EXE` = `C:\Windows\System32\scrnsave.scr`.
- **Prérequis** : aucun.
- **Limite** : s'applique à toutes les sessions interactives ciblées, y compris
  les administrateurs si la GPO leur est liée.

### 3.12 `shell-restrictions` — Interface Windows

- **Paramètres** : `disableRunDialog`, `disableWinKeys`, `hidePowerCommands`,
  `disableLockWorkstation` (booléens).
- **Écritures** :
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer`
    → `NoRun`, `NoWinKeys`, `NoClose` (DWORD = 1 selon les options) ;
  - `HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System`
    → `DisableLockWorkstation` (DWORD = 1).
- **Prérequis** : aucun.
- **Limite** : gêne le dépannage de premier niveau ; prévoir une GPO de support
  ou un filtrage par groupe.

### 3.13 `smartscreen-enforcement` — SmartScreen

- **Paramètre** : `level` : `Block` ou `Warn`.
- **Écritures** :
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\System`
    → `EnableSmartScreen` (DWORD) = `1` ;
  - même clé → `ShellSmartScreenLevel` (REG_SZ) = `Block` ou `Warn`.
- **Prérequis** : Windows 10 1709+ pour le mode Block ; accès Internet sortant
  vers le service de réputation Microsoft.
- **Limite** : des logiciels internes non signés peuvent être bloqués.

### 3.14 `uac-hardening` — UAC

- **Paramètres** : `secureDesktop` (booléen), `standardUserBehavior`
  (`Refuser automatiquement` ou `Demander les informations d'identification`).
- **Écritures** dans
  `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` :
  - `EnableLUA=1` (UAC toujours actif),
  - `ConsentPromptBehaviorAdmin=2`,
  - `PromptOnSecureDesktop` = `1` ou `0` selon `secureDesktop`,
  - `ConsentPromptBehaviorUser` = `0` (refus automatique) ou `3`.
- **Prérequis** : aucun.
- **Risque** : moyen ; le refus automatique empêche les utilisateurs standards
  d'élever leurs privilèges, même avec un mot de passe administrateur.

### 3.15 `windows-defender-firewall` — Defender / pare-feu

- **Paramètres** : `firewallEnabled`, `realTimeProtectionEnabled` (booléens).
- **Écritures** :
  - `HKLM\SOFTWARE\Policies\Microsoft\WindowsFirewall\DomainProfile`
    → `EnableFirewall` = `1` ou `0` ;
  - `…\StandardProfile` → `EnableFirewall` = `1` ou `0` ;
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows Defender`
    → `DisableAntiSpyware` = `0` (activé) ou `1` (désactivé) ;
  - `…\Windows Defender\Real-Time Protection`
    → `DisableRealtimeMonitoring` = `0` ou `1`.
- **Prérequis** : aucun ADMX ; la protection contre la modification (Tamper
  Protection) peut ignorer certains réglages Defender.
- **Risque** : élevé lorsque les protections sont désactivées ; une
  confirmation de risque est demandée dans la console.

### 3.16 `windows-update` — Windows Update / WSUS

- **Paramètres** : `mode` (`Windows Update` ou `WSUS`), `wuServerUrl`,
  `wuStatusServerUrl`, `auOptions` (`2`, `3`, `4`).
- **Écritures** :
  - `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU`
    → `NoAutoUpdate=0`, `AUOptions` = `2/3/4` ;
  - en mode WSUS :
    `HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate`
    → `WUServer`, `WUStatusServer` (REG_SZ) et `UseWUServer=1`.
- **Prérequis** : serveur WSUS réellement disponible avec son URL exacte en
  mode WSUS.
- **Limite** : un redémarrage est généralement nécessaire ; l'application
  n'invente aucune URL.

---

## 4. Ce que l'application vérifie avant de déployer

- **Aucun poste** ou **aucun modèle** sélectionné → déploiement refusé.
- **Poste désactivé dans AD DS** → exclu, avec explication.
- **Portée Utilisateur sans compte standard** → GPO refusée (pas de déploiement
  silencieux sur les administrateurs).
- **Paramètre obligatoire manquant ou invalide** → refus à la matérialisation.
- **Modèle inconnu ou sans liaison** → refus explicite.
- **Échec de filtrage AD DS** → la GPO n'est pas liée (pas de fenêtre
  d'application large).
- **Échec de tous les liens** → résultat en échec, détail dans la console et
  l'audit.

## 5. Vérification sur les postes et retrait

- **Actualiser** : bouton `gpupdate /force` de la console du poste.
- **Vérifier** : bouton `Vérifier (gpresult)` ; la liste des GPO réellement
  appliquées est relue par `gpresult /r`.
- **Détacher** : page Déploiements, bouton de détachement par poste (retire le
  poste du groupe de filtrage puis relance `gpupdate`). Le retrait n'est
  possible que si la GPO a été déployée avec filtrage de sécurité ; sinon elle
  est liée à l'OU entière et doit être retirée de l'OU par un administrateur.
- Les deux dernières actions exigent **WinRM actif sur le poste client**. Un
  poste uniquement résolu en DNS (sans WinRM) peut recevoir la GPO au prochain
  cycle de stratégie, mais l'application ne peut pas le rafraîchir ni vérifier
  son RSoP à distance.

## 6. Scénario de recette recommandé

1. Créer une OU de test et y placer un poste client de laboratoire.
2. Déployer `control-panel-settings` avec filtrage de sécurité sur ce seul
   poste, puis se connecter avec un compte standard : Panneau de configuration
   et Paramètres doivent être bloqués.
3. Vérifier avec `gpresult /r` et la page « Console du poste ».
4. Redéployer avec `blockControlPanel=false` : le déploiement doit être refusé
   (aucune GPO vide).
5. Déployer `screensaver-lock` avec 120 s, vérifier l'écran de veille et le
   verrouillage après inactivité.
6. Tester le détachement, puis `gpupdate /force` et vérifier la disparition de
   la GPO dans `gpresult`.
