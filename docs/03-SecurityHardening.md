# ADOps GPO Manager — Référentiel de durcissement de sécurité (Étape 3)

Ce document est le **référentiel de durcissement** (hardening) du parc
Windows. Il sépare deux familles de mesures :

1. **Mesures automatisables par l'application** — les modèles du
   catalogue (valeurs de stratégie de registre), documentés dans
   [`docs/02-GpoCatalog-Extensibility.md`](02-GpoCatalog-Extensibility.md) ;
2. **Mesures à configurer comme sections natives de GPO** — stratégie de
   mot de passe, droits utilisateur, stratégie d'audit avancée : elles
   ne sont pas représentables par de simples valeurs de registre et se
   configurent dans l'Éditeur GPMC (procédures ci-dessous).

Alignement sur les référentiels reconnus : **CIS Benchmarks** (Microsoft
Windows 11 Enterprise / Windows Server 2022), **Security Compliance
Toolkit (SCT)** de Microsoft, **guide d'hygiène informatique ANSSI**
(mesures 7 à 42) et **NIST SP 800-53** (AC, AU, CM).

## 1. Modèle de déploiement : OU et superposition de GPO

Arborescence cible (à créer dans `Utilisateurs et ordinateurs Active
Directory`) :

```
domaine.local
├── _Groupes                     (groupes de sécurité de filtrage)
├── _Postes
│   ├── Postes-Standard          (GPO: Durcissement-Postes + Blocage-PanneauConfig)
│   └── Postes-Technique         (durcissement allégé, outillage conservé)
├── _Utilisateurs
│   ├── Utilisateurs-Standard    (GPO: Restrictions-Utilisateur + Blocage-PanneauConfig)
│   └── Administration           (aucune restriction utilisateur ; applications des postes)
└── _Serveurs
    ├── Serveurs-Membres
    └── Serveurs-Administration  (tier 0)
```

Principes :

- **Une GPO par thème** (une GPO « durcissement réseau », une GPO
  « restrictions shell »…) : le diagnostic par `gpresult` reste lisible
  et le retrait ciblé est possible sans toucher au reste.
- **Filtrage de sécurité** : les restrictions *utilisateur*
  (Panneau de configuration, shell, cmd) ne ciblent que le groupe
  `GG-Utilisateurs-Standard` ; les comptes d'administration ne sont
  **jamais** concernés.
- **Ordre de liaison** (priorité décroissante, LIFO) : les GPO de
  restriction sur les OU enfants priment sur les GPO de base du domaine.
- **Boucle de retour (loopback)** : à activer en mode *remplacer* sur
  les postes en libre-service/salles partagées pour que les
  restrictions machine s'appliquent quel que soit l'utilisateur
  connecté.
- **Comptes d'administration** : jamais dans les OU d'utilisateurs
  standard (séparation des tâches, type tiering).

## 2. Règle obligatoire — blocage du Panneau de configuration et des Paramètres

Implémentation : modèle `control-panel-settings` (portée *User*).

| Élément | Valeur |
|---|---|
| Stratégie officielle | « Interdire l'accès au Panneau de configuration et aux Paramètres PC » (ControlPanel.admx) |
| Chemin | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer` |
| Valeur | `NoControlPanel` = `1` (DWORD) |
| Effet | Le Panneau de configuration **et** l'application Paramètres (immersive) refusent le démarrage pour les utilisateurs standards |
| Exception administration | Lier la GPO aux OU d'utilisateurs standard uniquement, ou filtrer sur `GG-Utilisateurs-Standard` — les administrateurs gardent l'accès |

Complément optionnel (si l'on veut n'occulter que certaines pages au
lieu de tout bloquer) : valeur `SettingsPageVisibility` sur la même clé,
ex. `hide:sound;bluetooth`.

Vérification sur un poste ciblé (`gpupdate /force` puis) :

```powershell
gpresult /scope user /h "$env:TEMP\rsop.html"
# puis ouvrir rsop.html : la GPO doit figurer en « Appliqué »
Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' |
    Select-Object NoControlPanel   # attendu : 1
```

## 3. Stratégie de mot de passe et verrouillage de compte

À configurer dans une GPO liée **au domaine** (une seule : les
paramètres de mot de passe ne s'appliquent qu'à la racine du domaine ;
utiliser des *Fine-Grained Password Policies* pour des exceptions).

| Paramètre | Valeur recommandée | CIS |
|---|---|---|
| Longueur minimale du mot de passe | 14 caractères | 1.1.2 |
| Complexité | Activée | 1.1.3 |
| Mémorisation de l'historique | 24 mots de passe | 1.1.1 |
| Âge maximal | 365 jours (ANSSI : pas d'expiration forcée si longueur ≥ 12 + complexité) | 1.1.4/1.1.5 |
| Âge minimal | 1 jour | 1.1.6 |
| Verrouillage après | 5 tentatives invalides | 1.2.1 |
| Durée de verrouillage | 15 minutes | 1.2.2 |
| Réinitialisation du compteur | 15 minutes | 1.2.3 |

Chemin GPMC : `Configuration ordinateur → Stratégies → Paramètres
Windows → Paramètres de sécurité → Stratégie de comptes`.

## 4. Droits utilisateur (User Rights Assignment)

Section GPMC : `Configuration ordinateur → Stratégies → Paramètres
Windows → Paramètres de sécurité → Stratégies locales → Attribution des
droits utilisateur`.

| Droit (constante) | Stratégie | Attribution recommandée |
|---|---|---|
| `SeNetworkLogonRight` | Accéder à cet ordinateur depuis le réseau | Administrateurs, Utilisateurs authentifiés (retirer « Tout le monde », Invité) |
| `SeRemoteInteractiveLogonRight` | Autoriser l'ouverture de session via Services Bureau à distance | Administrateurs (+ bureau distant dédié si RDP légitime) |
| `SeDenyNetworkLogonRight` | Refuser l'accès depuis le réseau | Invité, compte local `Invité` du domaine |
| `SeDenyRemoteInteractiveLogonRight` | Refuser la connexion par RDP | Invités, `NT AUTHORITY\Local account` (sauf jump servers) |
| `SeDebugPrivilege` | Déboguer les programmes | Administrateurs uniquement (retirer tout autre) |
| `SeBackupPrivilege` / `SeRestorePrivilege` | Sauvegarder / Restaurer | Administrateurs + groupe de sauvegarde dédié |
| `SeTakeOwnershipPrivilege` | Prendre possession des objets | Administrateurs |
| `SeTrustedCredManAccessPrivilege` | Accès fiable au Gestionnaire d'identifiants | Aucun |
| `SeMachineAccountPrivilege` | Ajouter des postes au domaine | Groupe dédié (sinon la limite `ms-DS-MachineAccountQuota` s'applique) |

## 5. Stratégie d'audit avancée

Préambule : appliquer le modèle `audit-policy-subcategory` (valeur
`SCENoApplyLegacyAuditPolicy=1`, `HKLM\...\Policies\Microsoft\Windows\System`)
pour que les sous-catégories avancées priment sur la stratégie d'audit
héritée — sinon les paramètres « classe » de l'audit d'objet écrasent
les sous-catégories.

Ligne de conduite native (une GPO « Audit-Avance » ou `auditpol` en
forçage contrôlé) :

```powershell
# Référentiel de base (à exécuter localement ou via script de démarrage)
auditpol /set /subcategory:"Logon" /success:enable /failure:enable
auditpol /set /subcategory:"Logoff" /success:enable /failure:disable
auditpol /set /subcategory:"Account Lockout" /failure:enable
auditpol /set /subcategory:"Other Logon/Logoff Events" /failure:enable
auditpol /set /subcategory:"Security Group Management" /success:enable
auditpol /set /subcategory:"User Account Management" /success:enable /failure:enable
auditpol /set /subcategory:"Credential Validation" /success:enable /failure:enable
auditpol /set /subcategory:"Process Creation" /success:enable
auditpol /set /subcategory:"Registry" /failure:enable
auditpol /set /subcategory:"File Share" /failure:enable
auditpol /set /subcategory:"Sensitive Privilege Use" /success:enable /failure:enable
auditpol /set /subcategory:"Removable Storage" /success:enable /failure:enable

# Ligne de commande des processus dans l'événement 4688 (EID 4688 + 4688/CommandLine)
# GPMC : Configuration ordinateur → Modèles d'administration → Système → Audit de processus :
#   « Inclure la ligne de commande dans les événements de création de processus » = Activé
```

Vérification : `auditpol /get /category:*`.

Collecte : rediriger le journal `Sécurité` vers le SIEM/SI (ou
abonnements collecteurs WEF) — un audit sans collecte n'est qu'une
déclaration d'intention.

## 6. Cartographie des modèles automatisés

Les modèles ci-dessous sont déployables directement par l'application
(assistant du catalogue). Le chemin de preuve exact de chaque modèle est
dans son champ `supportNote`.

| Modèle | Famille CIS / ANSSI | Réglage |
|---|---|---|
| `control-panel-settings` | AC-6 / M31 — **règle obligatoire** | `NoControlPanel=1` : Panneau de configuration + Paramètres bloqués |
| `shell-restrictions` | AC-6 / M31 | `NoRun`, `NoWinKeys`, `NoClose`, `DisableLockWorkstation` |
| `cmd-registry-block` | AC-6 | `DisableCMD` (1/2), `DisableRegistryTools=1` |
| `powershell-execution-policy` | CM/AV | `EnableScripts` + `ExecutionPolicy` (recommandé : `RemoteSigned` en prod, `Désactivé` pour postes sensibles) |
| `uac-hardening` | AC-6 | `EnableLUA=1`, `ConsentPromptBehaviorAdmin=2`, `PromptOnSecureDesktop=1`, `ConsentPromptBehaviorUser=0` |
| `smartscreen-enforcement` | SI | `EnableSmartScreen=1`, `ShellSmartScreenLevel=Block` |
| `asr-exploit-guard` | SI | Règles ASR Defender (GUID, déploiement d'abord en mode audit `=2`) |
| `legacy-protocols-hardening` | IA/NW | `SMB1=0`, `EnableMulticast=0` (LLMNR), `LmCompatibilityLevel=5` (NTLMv2) |
| `rdp-lockdown` | AC | `fDenyTSConnections=1` ou NLA obligatoire (`UserAuthentication=1`) |
| `screensaver-lock` | AC (physique) | `ScreenSaveActive=1`, `ScreenSaverIsSecure=1`, `ScreenSaveTimeOut` (recommandé : 600 s) |
| `autorun-hardening` | SI/USB | `NoDriveTypeAutoRun=255`, `NoAutorun=1` |
| `removable-storage-restrictions` | SI/USB | `Deny_All` / `Deny_Write` sur la classe disques `{53f56307-…}` |
| `audit-policy-subcategory` | AU | `SCENoApplyLegacyAuditPolicy=1` (préambule de la section 5) |
| `windows-update` | MAJ | Rapport/différer les mises à jour |
| `windows-defender-firewall` | SC | Pare-feu Defender (profils, journalisation) |

## 7. Procédure de déploiement et de retrait

1. **Créer** la GPO depuis l'application (catalogue → modèle →
   paramètres → nom explicite, ex. `SEC-Postes-Restrictions-Shell`).
   L'assistant refuse une GPO liée avant création — la création est
   explicite.
2. **Lier** à l'OU cible depuis la vue Annuaire/Déploiements
   (`New-GPLink`, idempotent). Le rapport d'impact affiche le nombre
   d'objets touchés **avant** validation.
3. **Diffuser** : `gpupdate /force` côté client (ou attendre le cycle
   d'arrière-plan, 90 min ± 30).
4. **Vérifier** : `gpresult /h` sur un poste témoin de chaque OU + lire
   la valeur de registre prévue dans `supportNote`.
5. **Retrait** : délier la GPO (`Remove-GPLink`) — les valeurs de
   registre *de stratégie* disparaissent à la révision suivante
   (clé `Policies` gérée par le service GP, retour au comportement
   natif sans nettoyage manuel). Chaque opération est auditable dans la
   vue Journal de l'application.

## 8. Déploiement progressif recommandé

| Vague | Périmètre | Contenu |
|---|---|---|
| 0 | Groupe pilote (10-20 postes) | Règle obligatoire + autorun + écran de veille |
| 1 | Tous les postes | Durcissement UAC, SmartScreen, mots de passe, audit de base |
| 2 | Tous les postes | Restrictions shell/cmd, ASR (d'abord en mode audit), PowerShell `RemoteSigned` |
| 3 | Réseau/protocoles | SMB1, LLMNR, NTLMv2, RDP — vérifier la compatibilité applicative **avant** |
| 4 | Finitions | Stockage amovible, blocage d'URL, affinements |

Chaque vague : indicateur = tickets support + événements d'audit
4625/4688/ASR ; 15 jours d'observation minimum entre deux vagues.

## 9. Références

- CIS Benchmarks — Microsoft Windows 11 Enterprise & Windows Server 2022
- Microsoft Security Compliance Toolkit 1.0 (baseline GPO prêtes à importer, Policy Analyzer)
- ANSSI — Guide d'hygiène informatique (42 mesures)
- NIST SP 800-53 (AC, AU, CM, SI) ; NIST SP 800-171
- Microsoft Learn — Référence des paramètres de stratégie de groupe
  (ADMX : ControlPanel.admx, Explorer.admx, PowerShellExecutionPolicy.admx…)
