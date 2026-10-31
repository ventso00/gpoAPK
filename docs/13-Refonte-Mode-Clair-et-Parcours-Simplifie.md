# 13 — Refonte « mode clair » et parcours simplifié

Date : 22 octobre 2026. Objet : refonte du système visuel (thème clair professionnel) et
du parcours principal (un assistant unique en 4 étapes), suppression des concepts
annoncés mais indisponibles, reconstruction complète de l'exécutable.

---

## 1. Ce qui a été refait

### 1.1 Thème clair professionnel

`Resources/Styles.xaml` a été **réécrit intégralement** (60 ressources). Le thème sombre
« terminal sécurisé » a laissé place à une palette « slate + bleu » conforme WCAG AA.

| Rôle | Couleur | Clé |
|---|---|---|
| Fond de l'application | `#F4F6F9` | `WindowBackgroundBrush` |
| Surfaces (cartes, barre latérale) | `#FFFFFF` | `CardBackgroundBrush` / `SidebarBackgroundBrush` |
| Accent (actions principales) | `#2563EB` | `AccentBrush` |
| Texte principal / secondaire | `#0F172A` / `#64748B` | `TextPrimaryBrush` / `TextSecondaryBrush` |
| Succès / Alerte / Danger | `#059669` / `#D97706` / `#DC2626` | `SuccessBrush` / `WarningBrush` / `DangerBrush` |
| Bordures | `#E2E8F0` | `BorderBrush` |

Nouveautés du système visuel : boutons à coins arrondis avec États survol/pressé,
surfaces « muted », badges, panneaux, **terminal d'exécution** (fond sombre dans un
interface claire), onglets soulignés à l'accent, en-têtes de tableaux modernisés.

La fenêtre principale est désormais **adaptative** : elle se dimensionne sur la zone
de travail réelle (`SystemParameters.WorkArea`), ce qui évite le débordement sur les
écrans 1366×768.

### 1.2 Un seul parcours : l'assistant en 4 étapes

La navigation à 8 entrées a été ramenée à 3 entrées principales
(Tableau de bord · **Appliquer une GPO** · Comptes clients) ; le reste passe en
« outils avancés ». **L'assistant est la page d'accueil.**

```
┌─ 1 · CIBLES ─────────────┐  ┌─ 2 · STRATÉGIES ───────────┐  ┌─ 3 · VÉRIFICATION ─┐  ┌─ 4 · EXÉCUTION ─┐
│ tous les postes et       │  │ toutes les GPO du domaine   │  │ plan d'impact,     │  │ terminal en direct │
│ comptes clients du       │  │ + les 16 modèles du         │  │ liens à créer,     │  │ résultat par      │
│ domaine, multi-sélection │  │ catalogue                    │  │ filtre de sécurité │  │ opération,        │
│ recherche, CRUD local    │  │ [➕ Ajouter une GPO]         │  │ [▶ Exécuter]       │  │ comptes concernés  │
└──────────────────────────┘  └─────────────────────────────┘  └────────────────────┘  └──────────────────┘
```

| Étape | Contenu |
|---|---|
| **1 · Cibles** | Table des comptes clients (cocher/décocher, retrait `✕`, ajout manuel d'une cible), table des postes, boutons « Tout cocher », « Tout décocher », « Réinitialiser ». La sélection est **persistée localement** (table `assistant_targets`) : elle survit au redémarrage. |
| **2 · Stratégies** | Table unifiée : GPO existantes du domaine **et** modèles du catalogue, avec colonne « Origine », « Portée », « Risque ». Le bouton **➕ Ajouter une GPO** crée réellement une GPO dans le domaine (nom, description, clé de registre, valeur, type). |
| **3 · Vérification** | Plan d'impact (nombre de liens, postes, comptes), avertissements, option de **filtrage de sécurité** (groupe autorisé, administrateurs exclus), option de **rafraîchissement** des postes. |
| **4 · Exécution** | Terminal sombre en direct (une ligne par opération, avec préfixe `[OK]`, `[AVERT]`, `[ERREUR]`), résumé du résultat, table des **comptes concernés** par la GPO. |

### 1.3 Concepts annoncés mais indisponibles — état après refonte

| Concept | Avant | Après |
|---|---|---|
| Filtrage de sécurité par groupe | absent | **`SetGpoSecurityFilterCommand`** (`Set-GPPermission`) : le groupe autorisé est ajouté, le groupe « Administrateurs » est exclu |
| `gpupdate /force` sur les postes | absent | **`InvokeGpUpdateCommand`** (exécuté sur le poste via WinRM) |
| `gpresult` (vérification réelle) | absent | **`GetGpResultCommand`** : les GPO réellement appliquées sont relues et affichées |
| Création d'une GPO à la demande | absent | **`CreateGpoCommand`** (`New-GPO` + `Set-GPRegistryValue`) |
| Déploiement exprimé en « machines » | absent (OU uniquement) | **`PlanMachineDeploymentAsync`** : les cibles sont regroupées par OU → un lien par conteneur |
| CRUD sur les comptes concernés | absent | **Table `assistant_targets`** + ajout / modification / retrait / réinitialisation |
| `BackupsViewModel` (annoncé dans la doc) | inexistant | **supprimé de la documentation** (le concept n'a jamais existé) |
| Loopback processing / filtre WMI | absent | **documenté comme non couvert** (hors périmètre d'un déploiement par OU) |

---

## 2. Erreurs trouvées et corrigées pendant la refonte

| Erreur | Symptôme | Cause | Correctif |
|---|---|---|---|
| `Item has already been added. Key: 'BorderBrush'` | **Crash immédiat au démarrage** (code `0xE0434352`) | Deux ressources `BorderBrush` dans `Styles.xaml` — les clés de ressources ne sont contrôlées qu'à l'**exécution** WPF, pas à la compilation | Doublon supprimé + contrôle automatique des `x:Key` |
| `Parameter "parameter" cannot be of type System.String … Int32` | Boîte de dialogue d'erreur au clic sur une étape | `CommandParameter="1"` est une chaîne, la commande attend un `int` | Commande en `string` + conversion explicite |
| Les 4 panneaux d'étape superposés | Le terminal (noir) recouvrait l'étape 1 | `IsStep1`…`IsStep4` sont des propriétés calculées : elles ne notifient aucun changement | `OnPropertyChanged` explicite dans `OnCurrentStepChanged` (idem `StepTitle`) |
| `36 × ConfigureAwait(false)` dans les ViewModels | `NotSupportedException : CollectionView … from a thread different from the Dispatcher` | Les `ObservableCollection` étaient modifiées hors du thread d'interface | Suppression de tous les `ConfigureAwait(false)` : une vue WPF revient toujours sur son thread |
| `settings.json` jamais créé | Panneau de configuration invisible | Le fichier n'est écrit que si la cible change | Création au premier démarrage |

---

## 3. Reconstruction complète de l'exécutable

Le livrable a été **détruit puis reconstruit** :

1. Suppression de `publish\ADOps.GpoManager.App.exe`, des `.pdb`, du manifeste.
2. Suppression de **toutes les données locales** : `%LOCALAPPDATA%\ADOps\` (base d'audit, configuration, journaux).
3. `dotnet restore` → `dotnet build -c Release` → **`dotnet test` : 148 tests passés**.
4. `dotnet publish` (self-contained, single-file, win-x64) → 63,4 Mo.
5. Génération du manifeste d'intégrité SHA-256 par l'application elle-même.
6. Lancement et recette sur la VM : **0 erreur, 0 critique** au journal.

Le premier démarrage reconstruit intégralement l'environnement : base SQLite avec le
schéma à jour, `settings.json` avec la cible par défaut, journaux.

**Cible configurée** (`settings.json`, créé automatiquement) :

```json
{
  "serverFqdn": "ventso.sarytany.local",
  "serverAddress": "192.168.5.2",
  "domainFqdn": "sarytany.local",
  "domainNetbios": "SARYTANY",
  "executorMode": 0,
  "autoSyncEnabled": true
}
```

---

## 4. Recette du parcours

| # | Action | Attendu |
|---|---|---|
| 1 | Lancer `publish\Lancer-Application.cmd` en administrateur | La fenêtre s'ouvre **directement** sur l'assistant, thème clair |
| 2 | Lire la barre supérieure | « Session implicite : SARYTANY\Administrator (élevé) — domaine sarytany.local via ventso.sarytany.local » |
| 3 | Lire le message de synchronisation | « Synchronisation réussie : 3 OU, 2 ordinateurs, 5 comptes clients, 2 GPO, 2 liens. » |
| 4 | Étape 1 | Les tables « COMPTES CLIENTS DU DOMAINE » et « POSTES DU DOMAINE » sont peuplées ; cocher / décocher, retirer (`✕`), ajouter |
| 5 | « Suivant → » | Passage à l'étape 2 ; le titre en bas affiche « Étape 2 — Stratégies à appliquer » |
| 6 | Étape 2 | Les GPO du domaine **et** les modèles du catalogue sont listés ; « ➕ Ajouter une GPO » ouvre le formulaire de création |
| 7 | Étape 2 → « Suivant → » | Le plan est calculé ; passage à l'étape 3 avec le résumé d'impact |
| 8 | Étape 3 | Options de filtre de sécurité et de rafraîchissement ; « ▶ Exécuter » |
| 9 | Étape 4 | Le terminal défile ligne par ligne ; le résumé et la table des comptes concernés s'affichent |
| 10 | Journal | `%LOCALAPPDATA%\ADOps\GpoManager\logs\adops.log` : chaque opération est tracée et auditée |

---

## 5. Limites assumées

- Le **filtrage de sécurité** et le **rafraîchissement** des postes exigent que le groupe
  autorisé soit un DN valide et que **WinRM soit activé sur les postes clients**
  (et pas seulement sur le contrôleur). Sans WinRM client, les liens sont bien créés
  mais le rafraîchissement est signalé « non actualisé » — jamais annoncé comme réussi.
- La **synchronisation automatique** est activée par défaut (15 minutes) ; elle est
  désactivable dans *Paramètres*.
- Le déploiement reste **par conteneur (OU)** : c'est le modèle GPO. Le ciblage
  « poste par poste » n'existe pas dans Active Directory, et l'exclusion fine des
  comptes d'administration se fait par le filtre de sécurité.
