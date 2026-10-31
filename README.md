# ADOps GPO Manager

**Console d'administration centralisée des GPO (Stratégies de groupe) et de l'Active Directory** pour Windows, sans saisie manuelle de commandes PowerShell.

Application **WPF / MVVM / .NET 10** à thème clair professionnel, livrée en un **exécutable autonome `.exe` single-file (66,6 Mo, runtime .NET 10 inclus, compressé)** qui s'exécute sur n'importe quel poste Windows x64 **sans installation préalable**. Le binaire publié est vérifié par un manifeste **SHA-256** (`publish/integrity.manifest`).

---

## Table des matières
- [À propos](#-à-propos)
- [Fonctionnalités](#-fonctionnalités)
- [Architecture](#-architecture)
- [Prérequis](#-prérequis)
- [Installation & démarrage rapide](#-installation--démarrage-rapide)
- [Compilation depuis les sources](#-compilation-depuis-les-sources)
- [Génération de l'exécutable](#-génération-de-l'exécutable)
- [Connexion à un domaine réel](#-connexion-à-un-domaine-réel)
- [Sécurité](#-sécurité)
- [Démo & évaluation sans domaine](#-démo--évaluation-sans-domaine)
- [Documentation](#-documentation)
- [Tests](#-tests)
- [Limites connues](#-limites-connues)
- [Contribution / dépôt GitHub](#-contribution--dépôt-github)

---

## À propos
ADOps GPO Manager cible les administrateurs systèmes qui doivent **inventorier, cibler et déployer des stratégies de groupe** sur un domaine Active Directory, avec :

- un **état de santé de connexion** vérifié étape par étape (DNS, RSAT, WinRM, authentification, accès AD DS, capacité GroupPolicy),
- une **synchronisation** de l'inventaire (OUs, utilisateurs, ordinateurs, GPO, liens),
- un **catalogue extensible de modèles de stratégies** (fichiers JSON, sans code),
- un **déploiement audité** des liens de GPO (`New-GPLink` / `Remove-GPLink`),
- une **traçabilité complète** (journal SQLite + fichier, intégrité SHA-256 des binaires).

Deux modes d'exécution sont pris en charge et choisis automatiquement selon le poste :
- **Local (RSAT)** — les commandes s'exécutent sur le poste d'administration via les modules GPMC/AD, en **Kerberos** (recommandé).
- **Distant (WinRM)** — les commandes sont transportées vers le contrôleur via `Invoke-Command` (canal HTTPS 5986, repli HTTP 5985 explicite et journalisé).

---

## Fonctionnalités

### Parcours principal : de l'inventaire au déploiement (4 étapes)

L'application s'ouvre **directement** sur le parc réel, sans écran de connexion :

1. **Postes clients par département** — inventaire AD DS synchronisé, cartes par département, recherche, état DNS/WinRM, sélection multiple. Les contrôleurs de domaine et serveurs sont exclus des cibles.
2. **GPO à appliquer** — les **16 modèles du catalogue**, catégories, recherche, sélection multiple et compteurs de portée.
3. **Paramètres des GPO** — champs typés par modèle (booléen, énumération, texte, listes d'URL), valeurs par défaut et avertissements.
4. **Appliquer les GPO aux machines** — protocole complet, résumé du plan, métriques, avertissements, terminal d'exécution, résultat par GPO et validation des postes.

Le bouton d'exécution reste désactivé tant que le plan est incomplet. Chaque GPO est créée dans le domaine, filtrée par groupe AD DS (administrateurs exclus), liée aux OU réelles des postes sélectionnés, puis actualisée et vérifiée sur les postes via WinRM.

### Contrôle AD DS des postes clients
- Panneau **« Contrôle AD DS · cycle de vie du poste »** : activer/désactiver le compte, déplacer vers une OU, redémarrer, éteindre.
- **Garde-fous** : serveurs, DC et hôte local protégés ; OU `Domain Controllers` interdite comme cible ; compte désactivé non pilotable ; chaque action confirmée puis auditée.
- Actualisation `gpupdate /force`, vérification `gpresult /r`, onglets Registre et Journal d'événements du poste.
- Détail et procédure de recette : [`docs/14-Controle-ADDS-des-Postes.md`](docs/14-Controle-ADDS-des-Postes.md).

### Connexion & diagnostic
- **Session implicite** : pas d'écran de connexion — identité, domaine et contrôleur sont déduits de la session Windows.
- **Health Check progressif** animé (6 étapes), mode d'exécution recommandé, messages d'erreur traduits **en français** avec remédiation actionnable.
- **Aucun mot de passe n'est demandé ni conservé** : la session Windows de l'utilisateur est l'identité employée.

### Inventaire réel & navigation
- **Synchronisation immédiate au démarrage** (OU, ordinateurs, comptes, GPO, liens), puis manuel via **↻ Resynchroniser**.
- **Postes clients par département** : cartes groupées par OU, recherche, état DNS/WinRM, sélection multiple ; DC et serveurs exclus.
- **Console du poste & test réel** : cycle de vie du compte, déplacement d'OU, mise à jour et lecture RSoP, onglets Registre et Journal d'événements, commandes d'administration Windows (ADUC, `gpmc`, gestion de l'ordinateur).
- **Journal d'audit** filtrable : horodatage, action, cible, résultat et détail de chaque opération.
- **Paramètres** : cible du domaine, canal WinRM, rejeu de la détection de session, verrouillage d'inactivité.

### Gestion des GPO
- Liste complète des GPO du domaine (`Get-GPO -All`) et des **16 modèles** du catalogue, sélection multiple.
- **Création réelle** d'une GPO (nom, clé de registre, valeur, type) ou depuis un modèle.
- **Filtre de sécurité** par groupe AD DS (comptes d'administration exclus), **liens** vers les OU réelles, résultats par GPO et par poste.
- Actualisation `gpupdate /force` et vérification `gpresult /r` sur les postes via WinRM.

### Catalogue de modèles (extensible sans code)
- **16 modèles JSON versionnés** : restrictions shell, PowerShell, UAC, SmartScreen, ASR Defender, réseau (SMB1, LLMNR, NTLMv2, RDP/NLA), périphériques, veille, audit — tous matérialisés en écritures de registre réelles.
- Ajouter un type de GPO = **ajouter un fichier JSON** (schéma : `docs/templates/template.schema.json`).
- Référence exhaustive (paramètres, clés exactes, portée, prérequis) : [`docs/08-GpoCatalog-Reference.md`](docs/08-GpoCatalog-Reference.md).

### Transversal
- **Audit** de chaque opération dans `%LOCALAPPDATA%\ADOps\GpoManager\audit.db`.
- **Journal** fichier avec rotation et masquage des secrets : `logs\adops.log`.
- **Aucune saisie libre de PowerShell** : commandes typées, entrées validées puis échappées.
- **Vérification d'intégrité des binaires** au démarrage (manifeste SHA-256).

---

## Architecture

```
gpo-main/
├── ADOps.GpoManager.sln            Solution (7 projets)
├── Directory.Build.props           Propriétés MSBuild partagées
├── Directory.Packages.props        Versions NuGet centralisées
├── build.bat / publish.bat         Scripts de compilation / publication
├── Lancer-Production.cmd           Lanceur protégé (anti double-instance)
│
├── backend/                        Logique métier (aucune dépendance UI)
│   ├── ADOps.GpoManager.Core/          Modèles, commandes PowerShell typées, validation, contrats, sécurité
│   ├── ADOps.GpoManager.PowerShell/    Exécuteurs local / WinRM, parsing sortie, interpréteur d'erreurs FR/EN
│   ├── ADOps.GpoManager.ActiveDirectory/ Inventaire AD DS, annuaire, découverte réseau, déploiement GPO, diagnostics
│   ├── ADOps.GpoManager.Infrastructure/ Persistance SQLite (audit, cache), journalisation, configuration JSON
│   └── ADOps.GpoManager.Templates/     Catalogue de modèles de stratégies (JSON embarqués)
│
├── frontend/
│   └── ADOps.GpoManager.App/       WPF / MVVM : session implicite, tableau de bord, annuaire, parc, comptes clients, GPO, déploiements, catalogue, audit, paramètres
│
├── tests/
│   └── ADOps.GpoManager.Tests/     xUnit (161 tests : catalogue, validation, sécurité, transport WinRM, contrôle AD DS)
│
├── docs/                           Documentation technique + maquettes HTML
└── publish/                        Sortie de publication (générée par publish.bat, non commitée)
```

**Couches & injection de dépendances** — `frontend` ne référence que les **interfaces** de `Core.Services` ; l'implémentation est câblée dans `Bootstrapper.cs` (conteneur `Microsoft.Extensions.DependencyInjection`). Le `PowerShell` et l'`ActiveDirectory` restent testables hors domaine grâce à cette inversion.

---

## Prérequis

| Besoin | Détail |
|---|---|
| **Système** | Windows 10/11 ou Windows Server 2016+ (x64) |
| **Exécuter l'app** | Rien à installer (runtime .NET 10 embarqué dans l'exe) |
| **Poste d'administration** | **Joint au domaine** (la session Windows est l'identité employée) |
| **Cible par défaut** | `ventso.sarytany.local` (`192.168.5.2`) — domaine `sarytany.local`, modifiable dans **Paramètres ▸ Cible du domaine** |
| **Compilateur** | SDK **.NET 10** uniquement |
| **Élévation** | Lancer **en administrateur** pour l'écriture des liens GPO |
| **Canal d'exécution** | **au moins un** des deux : RSAT local **ou** WinRM sur le DC |

**RSAT local** (recommandé, Kerberos) :
```powershell
Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0
Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0
```

**WinRM distant** (sur le contrôleur de domaine, une seule fois) :
```powershell
Enable-PSRemoting -Force
```

Ports réseau : **53** (DNS), **389/636** (LDAP/LDAPS), **445** (Sysvol/RPC), **5985/5986** (WinRM).

---

## Installation & démarrage rapide

1. Récupérer l'exécutable publié : `publish\ADOps.GpoManager.App.exe` (ou via `publish.bat`).
2. Double-cliquer **`Lancer-Production.cmd`** (ou exécuter l'exe) — **en tant qu'administrateur**.
3. C'est tout : **il n'y a plus d'écran de connexion**. L'application utilise le **compte Windows de la session courante**.
4. Au démarrage : détection du domaine et du contrôleur (API Netlogon) → contrôle de santé → **synchronisation immédiate** de l'inventaire (OU, ordinateurs, **comptes clients**, GPO, liens).
5. Le bouton **⟳ Synchroniser** (barre supérieure) relance la collecte à la demande.

> Le lanceur refuse un second lancement simultané afin de protéger `audit.db` / `settings.json` en cours d'écriture.

### Session implicite (remplacement de l'écran de connexion)

| Élément | Comportement |
|---|---|
| Identité | Le jeton de la session Windows courante — **aucun identifiant saisi, aucun mot de passe demandé ni stocké** |
| Domaine / contrôleur | Détectés automatiquement (Netlogon `DsGetDcName`), repli sur `USERDNSDOMAIN` |
| Mode d'exécution | **Local (RSAT)** avec le compte Windows de la session (recommandé) |
| Élévation | Contrôlée au démarrage : un avertissement explicite est émis si la session n'est pas élevée (les liens de GPO l'exigent) |
| Verrouillage d'inactivité | La console est masquée ; un déverrouillage explicite est demandé. Quitter la fenêtre de verrouillage ferme l'application |
| Reprise | **Paramètres ▸ Rejouer la détection de session** (après un changement de réseau ou l'octroi de nouveaux droits) |

---

## Compilation depuis les sources

```bat
build.bat
```
ou :
```powershell
dotnet restore ADOps.GpoManager.sln
dotnet build   ADOps.GpoManager.sln -c Release
```
Exécutable de développement : `frontend\ADOps.GpoManager.App\bin\Release\net10.0-windows\ADOps.GpoManager.App.exe`.

---

## Génération de l'exécutable

```bat
publish.bat
```
Équivaut à :
```powershell
dotnet publish frontend\ADOps.GpoManager.App\ADOps.GpoManager.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:PublishTrimmed=false `
  -o publish
```
Puis génération du **manifeste d'intégrité** :
```
publish\ADOps.GpoManager.App.exe --generate-integrity-manifest
```
**Résultat** : `publish\ADOps.GpoManager.App.exe` — fichier unique de **66,6 Mo** (63,5 Mio), runtime inclus. Le manifeste généré contient l'empreinte SHA-256 du binaire.

> Les antivirus signalent parfois les exécutables single-file auto-extractibles : ajouter une exclusion si nécessaire.

---

## Connexion à un domaine réel

L'application **n'ouvre plus d'écran de connexion** : elle travaille avec la session Windows courante.

- Le poste d'administration doit être **joint au domaine** et la session ouverte avec un compte ayant les droits sur les GPO (membre de **Domain Admins**, ou délégation GPMC sur les OU ciblées).
- Le domaine et le contrôleur sont **détectés automatiquement** (API Netlogon). Le FQDN du DC est préféré : Kerberos exige le SPN `HTTP/<fqdn>`.
- Si le Health Check échoue sur **WinRM** **et** que les modules **RSAT sont absents**, l'application n'a **aucun canal d'exécution** : installer RSAT (mode local) **ou** activer `Enable-PSRemoting` sur le DC (mode distant). Le message d'échec nomme explicitement ces deux correctifs.
- **Canal WinRM** réglable dans **Paramètres ▸ Canal WinRM** : *Négocier* (HTTPS 5986 puis repli HTTP 5985 journalisé), *HTTPS strict* (production), *HTTP en clair* (lab isolé uniquement).
- **Horloge** : un décalage > 5 min entre le poste et le DC casse l'authentification Kerberos.
- **Rejouer la détection** : **Paramètres ▸ Rejouer la détection de session** (changement de réseau, promotion de poste, nouveaux droits).

Détail complet du flux de synchronisation et d'application : [`docs/09-Sync-et-Application-Reelle.md`](docs/09-Sync-et-Application-Reelle.md).
Mise en service progressive sur un domaine de production (phases, vagues de déploiement, rollback, dépannage) : [`docs/10-Feuille-de-Route-Production.md`](docs/10-Feuille-de-Route-Production.md).

---

## Sécurité

- **Aucun mot de passe demandé ni persisté** : l'identité est celle du jeton de la session Windows courante ; transit distant par blob **DPAPI via stdin** (jamais en argument de ligne de commande, jamais loggé).
- **Commandes typées uniquement** : scripts passés en `-EncodedCommand` (Base64 UTF-16) ; noms/URN validés (`DomainValidator`, `DistinguishedNameValidator`) puis échappés (`InputSanitizer`).
- **Audit** de chaque opération modifiante (utilisateur, horodatage UTC, cibles, résultat) dans SQLite.
- **Intégrité binaire** : manifeste SHA-256 vérifié au démarrage, alerte en cas d'altération.
- **Journaux sanitisés** : secrets masqués, rotation 5 Mo × 5 fichiers.
- **Verrouillage après inactivité** : la console est masquée et exige un déverrouillage explicite ; quitter la fenêtre de verrouillage ferme l'application.

---

## Démo & évaluation sans domaine

L'application **travaille sur un domaine réel** (aucune donnée fictive n'est simulée dans l'exe) et **démarre sans écran de connexion** : sans domaine joignable, elle s'ouvre en mode dégradé sur le cache local et le signale dans la barre supérieure.

Pour **découvrir l'interface et les flux sans infrastructure AD**, deux maquettes HTML interactives sont fournies dans `docs/` :

| Fichier | Contenu |
|---|---|
| [`docs/preview.html`](docs/preview.html) | Prototype complet de l'application (sync animée, postes clients, GPO, déploiement) |
| [`docs/simulateur.html`](docs/simulateur.html) | Simulateur de catalogue GPO + déploiement + console client |
| [`docs/design-login-pro/`](docs/design-login-pro/) | Maquette de l'ancien écran de connexion (archive — l'écran n'existe plus dans l'exe) |

Ouverture locale :
```
cd docs
python -m http.server 4173
# puis http://127.0.0.1:4173/preview.html
```
> Ces maquettes sont des **outils de démonstration**, sans connexion à un vrai domaine. Les identifiants qui y apparaissent (`demo` / `demo.local` / `dc1.demo.local`) sont fictifs.

---

## Documentation

| Fichier | Description |
|---|---|
| `docs/01-Architecture.md` | Architecture détaillée (couches, DI, flux de données) |
| `docs/02-GpoCatalog-Extensibility.md` | Ajouter un modèle de stratégie (JSON, sans code) |
| `docs/03-SecurityHardening.md` | Référentiel de durcissement AD/GPO |
| `docs/04-ApplicationSecurity.md` | Transport WinRM, secrets, audit, intégrité binaire |
| `docs/05-Lab-VMware.md` | Mise en route d'une plateforme de test (Server 2019 + client) |
| `docs/07-Audit-et-Recette-Pre-HyperV.md` | Procédure de recette |
| `docs/08-GpoCatalog-Reference.md` | **Référence exhaustive des 16 modèles embarqués** (paramètres, clés de registre, portée, prérequis, limites) |
| `docs/09-Sync-et-Application-Reelle.md` | Flux de synchronisation et d'application réelle |
| `docs/10-Feuille-de-Route-Production.md` | **Feuille de route de mise en service sur un vrai domaine** (phases, vagues, rollback, dépannage) |
| `docs/11-Cahier-des-Charges-VM-Architecture-Cible.md` | **Diagnostic + architecture cible** : suppression de la connexion, bouton Synchroniser, vue « comptes clients », mode hors-ligne, application aux machines |
| `docs/12-Diagnostic-Quitte-Apres-Synchronisation.md` | **Diagnostic de la fermeture après synchronisation** (cible ignorée, collecte lente, verrouillage de session, défauts trouvés en exécution réelle) et tableau de bord synchronisé |
| `docs/13-Refonte-Mode-Clair-et-Parcours-Simplifie.md` | **Refonte complète** : thème clair professionnel, assistant en 4 étapes, concepts annoncés mais indisponibles, reconstruction de l'exécutable |
| `docs/14-Controle-ADDS-des-Postes.md` | **Contrôle AD DS des postes clients** : activation/désactivation, déplacement d'OU, redémarrage/extinction, garde-fous, recette |
| `docs/15-Analyse-Projet-A-Z.md` | **Analyse du projet A→Z** : architecture, flux, sécurité, constats d'exécution réelle, risques résiduels, checklist de recette |
| `docs/16-Procedure-Deployment-GPO-Client.md` | **Déploiement des GPO et validation client** : ciblage hybride, loopback Merge, filtrage AD DS, script PowerShell nettoyé, procédure `gpupdate /force` + `gpresult /r /scope computer` |

---

## Tests

```powershell
dotnet test ADOps.GpoManager.sln -c Release
```
**161 tests xUnit** (matérialisation des 16 modèles et de toutes leurs valeurs d'énumération, validation des entrées, sécurité, transport WinRM, intégrité, session implicite, contrôle AD DS).

---

## Limites connues

- **Création** et **mise à jour** de GPO depuis un modèle ou une définition manuelle ; le **renommage** et la **suppression** d'une GPO existante ne sont pas exposés.
- Le déploiement est réalisé **par conteneur (OU)** : c'est le modèle GPO. Filtre de sécurité par groupe : oui. Filtrage WMI : non. Loopback processing (mode « Merge ») : **oui**, automatiquement activé pour toute GPO de portée Ordinateur afin que ses paramètres machine s'appliquent sur le poste où l'utilisateur ciblé ouvre une session.
- Le **rafraîchissement** (`gpupdate /force`), la **vérification RSoP** (`gpresult`) et les **actions de puissance** exigent **WinRM actif sur les postes clients**, pas seulement sur le contrôleur. Dans le lab actuel, les deux clients ne répondent qu'au DNS : les GPO sont créées/liées, mais la validation distante reste indisponible jusqu'à activation de WinRM.
- Le **rapport d'impact** est calculé depuis l'appartenance d'OU (sous-arbre) ; le RSoP **réel** est lu par poste via `gpresult`.
- Les modèles de portée **Utilisateur** ne s'appliquent qu'aux comptes utilisateurs des OU liées ; le panneau de contrôle AD DS agit sur les **comptes d'ordinateur**.
- Double-hop Kerberos non géré (le compte explicite transite par DPAPI via WinRM).
- Les `targetDn` du fichier `gpo_catalog.json` sont des **exemples indicatifs** : au déploiement, la cible réelle est l'OU **sélectionnée par l'administrateur**, pas le DN du modèle.
- Les actions **redémarrer / éteindre** refusent les systèmes serveur, les contrôleurs de domaine et l'hôte local ; elles s'adressent aux postes clients.

---

## Contribution / dépôt GitHub

Le dépôt est prêt pour un push :

- **`.gitignore`** : exclut `bin/`, `obj/`, `publish/`, les artefacts (`*.dll`, `*.exe`, `*.pdb`, `*.zip`), les journaux, la base locale et tout secret (`*.env`, `*.pfx`, `secrets.json`).
- Seuls les **sources, scripts, tests et documentation** sont versionnés (les artefacts de build `bin/`, `obj/`, `*.dll`, `*.pdb`, `*.exe` previously commités ont été retirés du suivi).
- Aucune donnée confidentielle n'est présente (l'export de session `full-project-access-request.json` a été supprimé).

```bash
git add .
git commit -m "Nettoyage, documentation et préparation pour la production"
git branch -M main
git remote add origin <url-du-depot>
git push -u origin main
```

---

## Licence / contact
Projet livré en l'état pour un usage d'administration Active Directory. Voir `docs/` pour le cadrage métier complet.
# gpo-sarytany
