# Analyse du projet A → Z et état de la refonte

Document de synthèse : ce qui existe, comment les flux fonctionnent, ce qui a
été corrigé pendant la refonte « mode démo → mode réel », ce qui reste limité
et comment recetter l'ensemble.

Dernière vérification d'exécution : **23 octobre 2026**, domaine de recette
`sarytany.local` (contrôleur `ventso.sarytany.local`, `192.168.5.2`).

---

## 1. Objectif et périmètre

L'application doit permettre à un administrateur de :

1. découvrir la session Windows, le domaine et le contrôleur sans saisir de
   mot de passe ;
2. synchroniser l'inventaire AD DS (OU, ordinateurs, comptes, GPO, liens) ;
3. sélectionner des **postes clients** par département ;
4. sélectionner une ou plusieurs des **16 stratégies** du catalogue ;
5. paramétrer ces stratégies et visualiser leur impact ;
6. **créer, filtrer et lier les GPO réelles**, puis actualiser et vérifier les
   postes ;
7. piloter le cycle de vie AD DS des postes (compte, OU, alimentation) ;
8. tracer chaque opération dans un audit local.

La refonte conserve la direction artistique de `docs/preview.html` (thème clair,
vert émeraude, cartes, tableaux, console) tout en supprimant toute donnée
fictive : l'exécutable travaille exclusivement sur l'AD réel.

---

## 2. Cartographie du dépôt

```
ADOps.GpoManager.sln
├── backend/
│   ├── Core/             modèles, commandes PowerShell typées, validation,
│   │                     contrats de services, options de sécurité
│   ├── PowerShell/       exécuteurs local/WinRM, parsing JSON, erreurs FR
│   ├── ActiveDirectory/  inventaire, annuaire, GPO, déploiement, diagnostic,
│   │                     contrôle des postes (ComputerControlService)
│   ├── Infrastructure/   SQLite (audit, cache), journalisation, configuration,
│   │                     détection de session, intégrité
│   └── Templates/        catalogue JSON + liaisons de registre exécutables
├── frontend/App/         WPF .NET 10, MVVM, vues et styles de la console
├── tests/                xUnit (156 tests)
├── docs/                 architecture, sécurité, catalogue, recettes
├── build.bat / publish.bat / Lancer-Production.cmd
└── publish/              exécutable autonome (généré)
```

**Dépendances** : `App → Core/ActiveDirectory/Infrastructure/Templates`,
`ActiveDirectory → Core/PowerShell`, `Infrastructure → Core`, `Templates → Core`.
Aucune dépendance de `ActiveDirectory` vers `Infrastructure`. L'interface ne
connaît que les contrats de `Core.Services`, câblés dans `Bootstrapper.cs`.

---

## 3. Flux de bout en bout

### 3.1 Démarrage

1. `App.OnStartup` construit le conteneur DI et la fenêtre principale.
2. La session Windows est détectée via `USERDNSDOMAIN` + Netlogon
   (`DsGetDcName`) ; le mode local (RSAT) est préféré, sinon distant (WinRM).
3. La session est écrite dans `ConnectionState`, avec domaine, DC, identité et
   état d'élévation. Aucun mot de passe n'est demandé.
4. Le cache SQLite est ouvert (`%LOCALAPPDATA%\ADOps\GpoManager`) et
   l'inventaire est synchronisé immédiatement.
5. Le journal d'audit enregistre `ApplicationStarted`, `Login`,
   `InventorySync`.

### 3.2 Inventaire

- `ListComputersCommand`, `ListUsersCommand`, `ListOusCommand`,
  `ListGposCommand`, `ListGpoLinksCommand` renvoient du JSON parsé.
- Les résultats alimentent le cache et les collections de l'UI.
- Les contrôleurs de domaine et systèmes serveur sont **exclus des cibles de
  déploiement et de la console client** (`IsClientComputer` + garde-fous
  service).
- Les machines sont regroupées par département (nom d'OU), avec recherche,
  sélection multiple et résumé (postes / en ligne / départements).

### 3.3 Parcours en 4 étapes (interface réelle)

| Étape | Page | Contenu |
|---|---|---|
| 1 | Postes clients par département | cartes par département, sélection multiple, état DNS/WinRM, bouton Console |
| 2 | GPO à appliquer | 16 modèles, catégories, recherche, sélection, compteurs de portée |
| 3 | Paramètres des GPO | champs typés par modèle (booléen, énumération, texte, listes) |
| 4 | Appliquer les GPO aux machines | protocole, résumé, métriques, avertissements, console, exécution |

Le bouton d'application reste désactivé tant que le plan est incomplet. Les
avertissements de WinRM indisponible sont affichés avant toute action.

### 3.4 Déploiement d'un modèle

Détaillé dans `docs/08-GpoCatalog-Reference.md` §1 :

`Materialize` → `ApplyGpoTemplateCommand` (`New-GPO`,
`Remove-GPRegistryValue`, `Set-GPRegistryValue`) → `Set-GpoTargetingCommand`
(groupe + `Set-GPPermission`) → `LinkGpoCommand` (`New-GPLink`) →
`gpupdate /force` + `gpresult /r` → audit.

Les GPO sont liées aux **OU réelles des postes sélectionnés** (ou des comptes
standards pour les modèles Utilisateur), jamais aux DN d'exemple des fiches
JSON. Le filtrage de sécurité est créé **avant** la liaison.

### 3.5 Console du poste et cycle de vie AD DS

Détaillé dans `docs/14-Controle-ADDS-des-Postes.md` :

- activation / désactivation du compte (`Enable-ADAccount` /
  `Disable-ADAccount`) ;
- déplacement d'OU (`Move-ADObject`), avec liste d'OU filtrée (Domain
  Controllers exclue) et choix explicite ;
- redémarrage / extinction (`shutdown.exe` via WinRM, délai 15 s) ;
- `gpupdate /force`, `gpresult /r`, onglets Registre / Journal d'événements ;
- confirmations, garde-fous serveur/DC, audit complet.

### 3.6 Audit

- SQLite `audit.db` : action, serveur, domaine, cible, résultat, message,
  durée, identifiant d'opération, détails.
- Journal fichier `logs\adops.log` avec rotation et masquage.
- Page Audit : tableau lisible à 1024 px, message multi-ligne, métadonnées
  (utilisateur, serveur, durée) et infobulle avec l'identifiant d'opération.

---

## 4. Sécurité

| Mesure | Implémentation |
|---|---|
| Aucun mot de passe stocké | session Windows courante ; handoff DPAPI si distant |
| Commandes typées | classes `PowerShellCommand`, `-EncodedCommand`, JSON |
| Validation des entrées | `DomainValidator`, `DistinguishedNameValidator`, `InputSanitizer` |
| Pas de shell libre | aucune saisie PowerShell utilisateur |
| Portée limitée | DC/serveurs exclus des cibles ; OU Domain Controllers interdite |
| Confirmations | chaque action AD/puissance demande un accord explicite |
| Audit | toute opération modifiante, y compris refus et annulations |
| Intégrité | manifeste SHA-256 vérifié au démarrage |
| Journalisation | rotation, secrets masqués |

La console ne se substitue pas à une solution PAM : un compte de session
compromis conserve les droits de ce compte. Le principe de moindre privilège
reste de mise (délégation GPMC/OU plutôt que Domain Admin permanent).

---

## 5. Constats de l'exécution réelle

Environnement observé :

- 5 unités d'organisation, 3 ordinateurs (le DC `VENTSO` + `CLIENT1` et
  `CLIENT2`), 6 comptes clients, 3 GPO, 8 liens ;
- `CLIENT1` (`192.168.5.15`) et `CLIENT2` (`192.168.5.7`) sont **résolus en
  DNS mais sans WinRM** ; leur dernier état est « DNS seul » ;
- le DC est exclu des cibles client et de la console de cycle de vie ;
- la session est `SARYTANY\Administrator`, élevée, mode local RSAT.

Conséquences vérifiées :

- la synchronisation, l'inventaire, la sélection, le catalogue et la préparation
  du déploiement fonctionnent ;
- le déploiement réel créerait et lierait les GPO, mais `gpupdate`/`gpresult`
  échouent sur ces deux postes tant que WinRM n'est pas activé ;
- les actions AD (compte, OU) fonctionnent via RSAT local ; les actions de
  puissance nécessitent WinRM et échouent proprement sinon ;
- aucune modification AD n'a été exécutée pendant la recette : les
  confirmations ont été ouvertes puis annulées, le compte est resté activé.

---

## 6. Corrections livrées pendant la refonte

- Reconstruction de l'interface WPF alignée sur le prototype HTML : barre
  latérale 258 px, jetons de couleur, pages en 4 étapes, cartes, tableaux,
  console, page Audit lisible, page Paramètres.
- Suppression des références à l'ancien mode démo ; l'EXE travaille sur les
  données réelles.
- Ajout des 16 liaisons de registre exécutables et de tests de matérialisation
  (aucun modèle vide).
- Correction des modes « Accès complet / Lecture seule / Bloqué » du stockage
  amovible lors d'un redéploiement (remise à zéro des valeurs opposées).
- Ajout de `EnableLUA=1` et de la valeur `PromptOnSecureDesktop=0` lorsque le
  bureau sécurisé est désactivé.
- Prise en charge réelle de `firewallEnabled=false` et
  `realTimeProtectionEnabled=false` (valeurs de désactivation écrites, plus de
  paramètre silencieusement ignoré).
- Ajout du panneau de cycle de vie AD DS, des commandes typées, du service,
  des confirmations et des garde-fous serveur/DC.
- Filtrage de la cible de déplacement (Domain Controllers exclue) et sélection
  par défaut neutre (« Choisir une OU cible »).
- Correction de l'état de session (`ConnectionState.Current`) qui affichait
  « DC : — ».
- Correction du défilement des pages (retour en haut à chaque changement
  d'étape) et des libellés tronqués dans les cartes de métriques.
- Audit : lignes auto-dimensionnées, message multi-ligne et métadonnées
  visibles, sans colonnes fixes qui débordent en 1024 px.
- Filtrage des colonnes de la console client (dernière connexion) et des textes
  d'aide à la largeur réelle.

---

## 7. Tests et build

- **156 tests xUnit**, 0 échec (`dotnet test -c Release`).
- Build solution : **0 erreur** (avertissements de nullabilité et d'analyse
  statique uniquement).
- Publication : `publish.bat` produit un exécutable **single-file
  self-contained win-x64** avec manifeste d'intégrité SHA-256.
- L'application a été lancée en mode Debug et en mode publié sur le domaine de
  recette ; la synchronisation réelle et la navigation dans les 7 vues ont été
  vérifiées par captures d'écran.

---

## 8. Risques résiduels et recommandations

1. **WinRM absent sur les clients** : activer `Enable-PSRemoting -Force` (ou
   une GPO de pare-feu/WinRM) pour bénéficier de l'actualisation, de la
   vérification et des actions de puissance.
2. **GPO existantes** : le catalogue crée des GPO nommées `ADOps — …` et peut
   réutiliser une GPO du même nom. Sur un domaine de production, valider dans
   un lab et utiliser le filtrage de sécurité.
3. **Modèles à risque élevé** (SMBv1/NTLM, PowerShell, pare-feu/Defender) :
   déployer par anneaux pilotes, après inventaire de compatibilité.
4. **Portée Utilisateur** : la console cible les comptes standards des
   départements des postes sélectionnés ; les administrateurs sont exclus par
   construction. Vérifier les exceptions métier.
5. **Détachement** : possible uniquement pour les déploiements filtrés. Une
   GPO liée sans filtre doit être retirée de l'OU par un administrateur.
6. **Double-hop Kerberos** non géré ; utiliser le mode local ou une délégation
   adaptée.
7. **Renommage/suppression de GPO** non exposés dans l'UI actuelle ; passer par
   GPMC pour ces opérations.

---

## 9. Checklist de recette finale

- [x] L'EXE démarre sans écran de connexion et détecte `sarytany.local` /
      `ventso.sarytany.local`.
- [x] La barre d'outils et la carte de session s'affichent correctement en
      1024 × 768.
- [x] 2 clients réels listés, DC exclu, états « DNS seul » visibles.
- [x] 16 modèles affichés, sélectionnables, paramétrables.
- [x] Le plan de déploiement affiche les métriques et les avertissements WinRM.
- [x] La console client affiche le panneau AD DS, la confirmation et les
      garde-fous ; l'annulation ne modifie rien.
- [x] L'OU Domain Controllers est absente des cibles de déplacement.
- [x] La page Audit est lisible et journalise les opérations.
- [x] 156 tests au vert.
- [x] Publication single-file + manifeste d'intégrité.
