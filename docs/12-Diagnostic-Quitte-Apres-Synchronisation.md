# 12 — Diagnostic : l'application se ferme après la synchronisation

Date : 22 octobre 2026. Contexte : VM VMware (session administrateur),
contrôleur `ventso.sarytany.local` / `192.168.5.2`, domaine `sarytany.local`.

---

## 1. Symptôme

> « L'app quitte automatiquement après la synchronisation, alors qu'elle doit
> afficher le tableau de bord synchronisé avec le serveur. »

Concrètement : la fenêtre principale disparaît (ou le processus se termine) au
moment où la collecte se termine, et l'utilisateur ne voit jamais le tableau de
bord peuplé.

---

## 2. Les quatre causes (toutes corrigées)

### Cause 1 — La cible configurée était ignorée *(cause racine principale)*

Avant la suppression de l'écran de connexion, l'application enregistrait le
contrôleur et le domaine choisis dans `%LOCALAPPDATA%\ADOps\GpoManager\settings.json`
(et dans la table `domain_profiles` de SQLite). La session implicite, écrite
ensuite, **ne lisait pas ce fichier** : elle retombait uniquement sur la
détection Netlogon.

Conséquence sur la VM : `DsGetDcName(NULL, NULL, …)` renvoie le contrôleur
*principal* du domaine, qui n'est pas forcément celui attendu. Si la détection
échoue, la session est abandonnée, la synchronisation ne part jamais, et
l'utilisateur ne voit qu'un tableau de bord vide — sans comprendre pourquoi.

**Correctif** — `SessionBootstrapper.ResolveServer()` applique désormais la
priorité suivante :

```
1. settings.json : ServerFqdn  (défaut : ventso.sarytany.local)
2. settings.json : ServerAddress (défaut : 192.168.5.2) → converti en FQDN
                   via l'API Netlogon (Kerberos refuse une cible IP)
3. Netlogon : découverte automatique du DC du domaine
4. Machine locale (dernier recours, avec avertissement explicite)
```

La cible résolue est **réécrite dans `settings.json`** : le démarrage suivant
est déterministe. Le domaine suit la même logique (configuration d'abord,
détection en repli).

### Cause 2 — La collecte des comptes était anormalement lente

`ListUsersCommand` projetait l'attribut `memberOf` **pour chaque compte** :

```powershell
Get-ADUser -Filter * -Properties …, memberOf | Select-Object …
```

`memberOf` est une rétro-liaison : Active Directory doit énumérer les groupes
de chaque utilisateur. Sur un domaine de taille moyenne, cette seule ligne
prend **plusieurs minutes** — et bloque la synchronisation entière.

**Correctif** — la classification « administrateur / standard » est obtenue par
une requête **unique et rapide** sur les groupes d'administration :

```powershell
Get-ADGroupMember -Identity "S-1-5-21-…-512"   # Domain Admins
Get-ADGroupMember -Identity "S-1-5-21-…-518"   # Enterprise Admins
Get-ADGroupMember -Identity "S-1-5-21-…-519"   # Administrateurs du DC
```

`AdOutputParser.ParseUsers(json, administratorDns)` applique ensuite
l'appartenance au set de DN. Un compte absent du set est « standard » : la
classification est un **fait observé**, jamais une supposition.

### Cause 3 — Le verrouillage automatique masquait la fenêtre pendant la collecte

`SessionGuard` verrouille la console après `security.sessionTimeoutMinutes`
(10 minutes par défaut) d'inactivité. Pendant une synchronisation longue,
l'administrateur ne touche pas la souris : le garde se déclenche, masque la
fenêtre (`Hide()`) et affiche la fenêtre de verrouillage.

**C'est le mécanisme exact du symptôme « l'application disparaît »** : la
fenêtre est cachée, l'utilisateur croit que l'application a quitté.

**Correctif** — `MainWindow` suspend le garde pendant une synchronisation :

```csharp
if (_viewModel.IsSyncing)
{
    _sessionGuard.ReportActivity();   // l'activité est réarmée
    SessionLockStatus.Text = "Session : synchronisation en cours";
    return;                            // pas de Tick() → pas de verrouillage
}
```

### Cause 4 — Une exception hors du thread UI tuait le processus

`DispatcherUnhandledException` ne couvre que le thread d'interface. Une
exception sur un thread secondaire (tâche de fond, continuation de `Task`)
arrête le processus **sans message**.

**Correctif** — `App.OnStartup` enregistre désormais trois filets :

| Filet | Rôle |
|---|---|
| `DispatcherUnhandledException` | Exception sur le thread UI → journalisée, **l'application reste ouverte** (`e.Handled = true`) |
| `AppDomain.CurrentDomain.UnhandledException` | Exception sur un thread secondaire → journalisée en `Critical` (la cause reste lisible) |
| `TaskScheduler.UnobservedTaskException` | Exception de tâche sans observateur → journalisée, `SetObserved()` |

Le journal est toujours dans `%LOCALAPPDATA%\ADOps\GpoManager\logs\adops.log`.

---

## 3. Défauts révélés par le lancement réel sur la VM (22/10/2026, 13:20)

Le premier lancement réel de la version corrigée a mis au jour **six défauts
supplémentaires**, tous reproduits puis corrigés. Ils sont consignés ici parce
que leur cause n'est pas devinable sans exécuter l'application.

| # | Symptôme observé | Cause racine | Correctif |
|---|---|---|---|
| 1 | Synchronisation « partielle : **0 comptes** » | `Get-ADUser -Properties AccountLocked` → **`ArgumentException : One or more properties are invalid`**. `AccountLocked` n'est pas un attribut projetable par le provider AD DS. | L'attribut est `lockoutTime` ; `AccountLocked` est recalculé en expression PowerShell (0 et `9223372036854775807` = non verrouillé). |
| 2 | Domaine détecté = `\\192.168.5.2` | La structure `DC_INFO` renvoyée par `DsGetDcName(NULL, NULL, …)` contient **le nom du contrôleur** (offset 0) puis **son adresse IP** (offset suivant) — **pas** le nom de domaine. La lecture était donc faussée. | `DcLocator.DiscoverPrimaryController()` lit le contrôleur et son adresse ; le domaine vient de `USERDNSDOMAIN` (source faisant autorité) ou est déduit des deux dernières étiquettes du FQDN. |
| 3 | Compte affiché `SARYTANY\SARYTANY\Administrator` | `DisplayText` préfixait le domaine alors que `UserName` était déjà qualifié. | `DisplayText` ne préfixe plus que si le nom ne contient pas déjà `\`. |
| 4 | Crash de layout : `Cannot find resource named 'TabControlStyle'` | `ComputerDetailView.xaml` référence `TabControlStyle` et `TabItemStyle`, **absents de `Styles.xaml`**. L'ouverture de la fiche d'un poste faisait échouer le rendu de la fenêtre. | Les deux styles ont été ajoutés (onglet accentué, en-tête de champ). |
| 5 | `NotSupportedException: This type of CollectionView does not support changes to its SourceCollection from a thread different from the Dispatcher thread` | **36 `ConfigureAwait(false)`** dans les ViewModels : les continuations reprenaient sur un thread du pool, et les `ObservableCollection` étaient modifiées hors du thread d'interface. | `ConfigureAwait(false)` supprimé de **tous** les ViewModels : une vue WPF doit toujours revenir sur son thread d'interface. |
| 6 | « 5 comptes clients (**0 administrateur**)" | `Get-ADGroupMember` ne renvoyant qu'**un** membre, `ConvertTo-Json` sérialise un **scalaire** (`"CN=…"`) et non un tableau : le parseur, qui ne lisait que tableaux et objets, ne voyait rien. | La commande émet des objets (`[PSCustomObject]@{ DistinguishedName = … }`) ; le parseur accepte désormais tableau, objet **et** scalaire. |
| 7 | Deux synchronisations concurrentes au démarrage | La page Tableau de bord déclenchait sa propre collecte alors que celle du démarrage était encore en cours. | Indicateur `ConnectionState.IsSyncInProgress` partagé : une seule collecte à la fois. |

### Ordre de grandeur mesuré

| Mesure | Avant correctif | Après correctif |
|---|---|---|
| Durée de la synchronisation | 2 s (partielle, 0 compte) | **2,3 s** (complète) |
| Comptes collectés | 0 | **5** (dont 1 administrateur) |
| Erreurs / critiques au journal | 1 `NotSupportedException` + 1 `XamlParseException` | **0** |

---

## 4. Ce que l'utilisateur voit désormais

### Barre supérieure

```
SESSION
Session implicite : SARYTANY\Administrateur (élevé) — sarytany.local via
ventso.sarytany.local (192.168.5.2)
Synchronisation en cours… — Ordinateurs : OK — 3 élément(s) lu(s).
```

### Tableau de bord — encart « SESSION ET SYNCHRONISATION AVEC LE DOMAINE »

| Information | Source |
|---|---|
| Domaine, NetBIOS, contrôleur (FQDN + IP), mode d'exécution, compte | Contexte de session |
| Résumé du contrôle de santé (6 étapes) | `HealthCheckReport.Summary` |
| Date/heure, état et durée de la dernière synchronisation | `SyncRunSummary` |
| Volumétrie : OU, machines, **comptes clients** (+ administrateurs), GPO, **liens** | Cache local SQLite |
| Avertissements de la collecte | `SyncResult.Warnings` |

Un bouton **⟳ Synchroniser** est présent sur le tableau de bord **et** dans la
barre supérieure. La progression est détaillée étape par étape
(`Unités d'organisation`, `Ordinateurs`, `Comptes d'administration`,
`Comptes clients`, `GPO du domaine`, `Liens de GPO`).

### Paramètres — nouvelle section « CIBLE DU DOMAINE »

| Champ | Défaut | Rôle |
|---|---|---|
| Contrôleur (FQDN) | `ventso.sarytany.local` | Cible prioritaire (Kerberos) |
| Adresse IP du contrôleur | `192.168.5.2` | Repli si le DNS de la VM ne résout pas le nom |
| Domaine (nom DNS) | `sarytany.local` | Domaine AD |
| NetBIOS du domaine | `SARYTANY` | Facultatif |

Le bouton **« Rejouer la détection de session avec cette cible »** recharge la
cible, rejoue le contrôle de santé **et relance la synchronisation** sur le
nouveau contrôleur.

---

## 5. Recette de vérification sur la VM

```powershell
# 1. Compiler et publier (OBLIGATOIRE : ne pas tester un exe antérieur)
cd <racine>\gpo-main
dotnet restore .\ADOps.GpoManager.sln
dotnet build   .\ADOps.GpoManager.sln -c Release
dotnet test    .\ADOps.GpoManager.sln -c Release
.\publish.bat

# 2. Lancer EN TANT QU'ADMINISTRATEUR
publish\Lancer-Application.cmd
```

**Contrôles** :

| # | Attendu |
|---|---|
| 1 | **Aucun écran de connexion** : la fenêtre principale s'ouvre directement. |
| 2 | Barre supérieure : « Session implicite : … — sarytany.local via ventso.sarytany.local (192.168.5.2) ». |
| 3 | La synchronisation démarre **immédiatement** et les 6 étapes s'affichent une à une. |
| 4 | **L'application reste ouverte** pendant et après la synchronisation (c'est le point du correctif). |
| 5 | Tableau de bord : l'encart affiche domaine, contrôleur, IP, durée, volumétrie (OU / machines / comptes / GPO / liens). |
| 6 | « Comptes clients » : la liste est peuplée, les comptes d'administration sont distingués. |
| 7 | `Settings ▸ Cible du domaine` : les 4 valeurs sont pré-remplies ; « Rejouer la détection » resynchronise. |
| 8 | Vérifier la cible retenue : `Paramètres ▸ Cible du domaine` puis `Get-GPO -Domain sarytany.local -All` en PowerShell. |
| 9 | Si un problème subsiste, lire `%LOCALAPPDATA%\ADOps\GpoManager\logs\adops.log` : toute exception y est tracée avec sa pile. |

---

## 6. Appliquer une GPO depuis l'état synchronisé

La chaîne est désormais entièrement alimentée par la synchronisation :

1. **⟳ Synchroniser** — OU, machines, comptes, GPO et liens sont collectés.
2. **Parc informatique** — les machines sont connues avec leur OU (donc leur
   cible GPO naturelle) et leur état de disponibilité.
3. **GPO du domaine** — la liste vient du même cache (plus de lecture directe).
4. **Automatiser la GPO** — assistant en 3 étapes : machines cibles → GPO →
   validation et application (`New-GPLink`, auditée).
5. **Comptes clients** — pour vérifier *qui* est concerné, et écarter les
   comptes d'administration du ciblage utilisateur.

> Point de vigilance inchangé : une GPO s'applique à un **conteneur** (OU), pas
> à un objet. Le ciblage « machines » est donc réalisé en liant la GPO à l'OU
> des machines sélectionnées, et non machine par machine. La Security Filtering
> par groupe reste à implémenter (lot P4 de `docs/11`).
