# Refonte de l'interface — livraison et recette

Date : **30 octobre 2026**.
Exécutable livré : `publish\ADOps.GpoManager.App.exe`, version `1.0.0+add2412`,
SHA-256 `2dcf4a7727d16d39fe58d7c3111bd81edf20d2e7645be51792a35c92c13e2300`
(66 612 410 octets, self-contained win-x64, fichier unique).

Version de secours conservée :
`publish\sauvegarde-avant-refonte-20261030-0622\ADOps.GpoManager.App.exe`,
SHA-256 `7488313b7b61dbefc86daf9cc809ea01e75aa06765bab12518fe521023fbb04e`
(66 600 033 octets, binaire du 30/10/2026 06:22, build d'avant refonte).

---

## 1. Analyse technique préalable

Les sources complètes étaient disponibles dans le dépôt : la refonte a donc été
faite **dans les sources XAML/C#**, sans jamais repartir de l'.exe.

| Question | Réponse vérifiée |
| --- | --- |
| Technologie de l'interface | **WPF** (XAML + C#), pas WinForms, pas WinUI, pas WebView2 |
| Cible | `.NET 10`, `net10.0-windows`, `UseWPF` |
| Pattern | MVVM avec `CommunityToolkit.Mvvm` 8.4.2 (`ObservableProperty`, `RelayCommand`) |
| Composition racine | `Microsoft.Extensions.DependencyInjection`, câblée dans `Bootstrapper.cs` |
| Comment l'.exe fonctionne | `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true` (script `publish.bat`) ; runtime .NET inclus, aucune installation préalable sur le poste cible ; `AssemblyIntegrityGuard` compare le SHA-256 du binaire à `integrity.manifest` au démarrage |
| Dépendances runtime | PowerShell 5.1 + RSAT (`ADModule`, `GroupPolicy`), WinRM (5985/5986), `Microsoft.Data.Sqlite`, `System.Security.Cryptography.ProtectedData` (DPAPI) |

Structure du dépôt (inchangée) :

```
backend/   Core · PowerShell · ActiveDirectory · Infrastructure · Templates
frontend/  ADOps.GpoManager.App   (vues XAML, ViewModels, Resources/Styles.xaml, Controls/)
tests/     ADOps.GpoManager.Tests (xUnit)
publish/   exécutable autonome généré
docs/      architecture, sécurité, catalogue, recettes
```

L'interface ne parle qu'aux contrats de `Core.Services` : elle ne contient aucun
code AD, aucun script PowerShell, aucune requête SQL.

---

## 2. Refonte graphique

### Design system (`Resources/Styles.xaml`)

Jetons de couleurs appliqués partout (thème clair, contraste AA) :

| Rôle | Jeton | Valeur |
| --- | --- | --- |
| Fond | `WindowBackgroundBrush` | `#F8FAFC` |
| Surface de carte | `CardBackgroundBrush` | `#FFFFFF` |
| Texte principal | `TextPrimaryBrush` | `#111827` |
| Texte secondaire | `TextSecondaryBrush` | `#64748B` |
| Séparateurs | `BorderBrush` | `#E2E8F0` |
| Accent | `AccentBrush` | `#2563EB` |
| Succès / Warning / Erreur | `SuccessBrush` / `WarningBrush` / `DangerBrush` | `#059669` / `#D97706` / `#DC2626` |

Typographie **Segoe UI Variable** (`AppFontFamily`, repli `Segoe UI`), police
mono (`MonoFontFamily`) pour les chemins, les DN et la console. Rythme
commun : `PagePadding`, `CardPadding`, `RadiusMedium` (8), cartes
`CardStyle` / `ElevatedCardStyle`, boutons `PrimaryButtonStyle` /
`SecondaryButtonStyle` / `DangerButtonStyle`, pastilles `Pill*Style`.
Animations discrètes : retrait de 1,5 % au clic, fondu des infobulles de
survol, aucun clignotement.

### Icônes

`Controls/LucideIcon.cs` + `Controls/LucideIconData.cs` : contrôle `Viewbox` qui
rend des tracés **Lucide** intégrés en XAML (aucune dépendance à une police
d'icônes, aucune image bitmap). **47 icônes** exposées par l'énumération
`LucideIconName`. Le trait hérite de la couleur du texte (`Stroke` lié à
`TextElement.Foreground`), donc l'icône suit automatiquement les états
survol/actif/désactivé. Un test (`LucideIconCatalogTests`) garantit que chaque
nom utilisé dans les XAML existe bien dans le catalogue.

### ComboBox Fluent

Les listes déroulantes gardaient l'habillage Aero classique (dégradé gris),
seule rupture visible avec le reste du thème. `ComboBoxStyle` a reçu un gabarit
complet (bouton bascule arrondi, chevron Lucide, `Popup` blanc avec ombre et
`ComboBoxItem` restylé) et `SelectStyle` en dérive. Vérifié à l'écran : la
partie déroulante est rendue et peuplée (voir §5).

---

## 3. Réorganisation des fonctionnalités

La navigation latérale est regroupée en 4 rubriques qui couvrent les 6 domaines
demandés. Chaque entrée pointe sur un état réel du `MainViewModel`.

| Domaine demandé | Entrée de navigation | Clé |
| --- | --- | --- |
| Tableau de bord | Tableau de bord | `overview` |
| Postes clients | Comptes utilisateurs ciblés | `machines` |
| Gestion des GPO | Gestion des GPO · Paramètres des GPO · Appliquer aux postes | `gpos` · `params` · `deploy` |
| Console des postes (vérification `gpresult`) | Postes clients & console | `clients` |
| Journal d'audit | Journal d'audit | `audit` |
| Paramètres / session | Paramètres & session | `settings` |

Le parcours de déploiement (étapes 1 → 4) reste le moteur existant : ciblage
des comptes, choix des stratégies, paramètres, application réelle. Seule la
présentation a changé.

---

## 4. Créateur — uniquement dans Paramètres → À propos

Carte « À propos » en bas de `Views/SettingsView.xaml`, alimentée par
`SettingsViewModel` :

- Logiciel : **ADOps GPO Manager**
- Créateur : **Ventso Rajaomanjato** — Fonction : **Créateur et développeur**
- Version, date du binaire, runtime et système : **lus à l'exécution** sur
  l'assembly et `RuntimeInformation` (aucune valeur saisie à la main)
- Technologies réellement utilisées : .NET 10, WPF + XAML,
  CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.DependencyInjection 10.0.12,
  PowerShell 5.1 via RSAT, WinRM 5985/5986, Microsoft.Data.Sqlite 10.0.12,
  DPAPI, icônes Lucide

Aucun de ces champs n'apparaît sur un autre écran.

---

## 5. Préservation du moteur métier

**Aucun fichier de `backend/` n'a été modifié par la refonte graphique.** Les
22 fichiers backend modifiés dans l'arbre de travail l'avaient été par la
correction précédente (CLIXML, IP opérationnelle, sortie `gpresult`, liste des
GPO appliquées) et sont couverts par les tests xUnit.

Sont intacts : connexion AD et détection de session, synchronisation de
l'inventaire, création/filtrage/lien des GPO, exécution PowerShell locale et
WinRM, garde-fous de droits, boîtes de confirmation avant opération sensible,
journal d'audit SQLite, verrouillage de session après inactivité
(`SessionGuard` + `SessionLockWindow`), contrôle d'intégrité du binaire.

Suppression de 12 commandes `GoToXxx` et 5 commandes `OpenXxx` : vérification a
été faite qu'elles étaient **déjà inaccessibles** dans la version d'origine —
`MainWindow.xaml` d'avant refonte n'hébergeait jamais `CurrentViewModel`, les 11
vues concernées (Parc, Directory, GpoList, Catalog, Deployments, GpoAssistant,
Diagnostics, ClientAccounts, ComputerDetail, DepartmentMapping, AutomateGpo)
étaient donc du code mort. Elles n'ont pas été réactivées (voir §7).

---

## 6. Tests réellement exécutés

### Compilation et tests unitaires

- `dotnet build frontend\ADOps.GpoManager.App\ADOps.GpoManager.App.csproj -c Debug`
  → **succès, 0 erreur** (avertissements MVVMTK0034 préexistants uniquement).
- `dotnet test tests\ADOps.GpoManager.Tests\ADOps.GpoManager.Tests.csproj`
  → **195 tests réussis, 0 échec, 0 skip** (durée 2 s).

### Validation visuelle

Cette machine ne fournit aucun contexte d'affichage exploitable
(`CopyFromScreen` échoue, `PrintWindow` rend une image vide pour WPF). Un
hors-écran a donc été validé en rendant **l'arborescence visuelle réelle** de
l'application (`RenderTargetBitmap`) depuis un hôte de test
(`.uicheck/uishoot`, outil de développement non livré), qui instancie la vraie
`MainWindow`, lit le cache SQLite local et navigue section par section. Aucune
écriture dans Active Directory n'est déclenchée par cet outil.

Le facteur d'échelle est simulé par un `LayoutTransform` + DPI `96 × facteur`,
ce qui reproduit exactement la mise en page à 125/150/200 %.

| Pass | Résolution | Captures | Résultat |
| --- | --- | --- | --- |
| 100 % | 1360 × 860 | `.uicheck/v3/` | conforme |
| 125 % | 1360 × 860 | `.uicheck/s125/` | conforme |
| 150 % | 1360 × 860 | `.uicheck/s150b/`, `.uicheck/s150c/` | conforme après correction |
| 200 % | 2000 × 1250 | `.uicheck/s200b/` | conforme après correction |
| fenêtre étroite | 1024 × 700 | `.uicheck/narrow/` | conforme |
| liste déroulante OU ouverte | 1360 × 860 | `.uicheck/combo/combo_ouvert.png` | conforme, 5 OU affichées |

Défauts relevés par ces passes et corrigés :

1. libellés d'indicateurs tronqués dans les cartes KPI (un `StackPanel`
   horizontal donne une largeur infinie à son enfant, le retour à la ligne ne
   s'activait jamais) → `DockPanel` + `TextWrapping="Wrap"` ;
2. en-têtes de colonnes du tableau d'étape 1 qui se chevauchaient → jeu de
   colonnes rééquilibré et marges droite ajoutées ;
3. horodatage de l'audit coupé à 150 % et 200 % → colonnes passées en
   pourcentage avec largeur minimale, troncature par points de suspension et
   infobulle ;
4. « Sans département » tronqué à 150 % → colonne DÉPARTEMENT élargie,
   infobulle ajoutée sur DÉPARTEMENT, EMAIL et la dernière connexion ;
5. listes déroulantes en habillage Aero → gabarit Fluent (voir §2).

### Démarrage de l'exécutable livré

`publish\ADOps.GpoManager.App.exe` lancé après remplacement : processus stable
(PID 9244, 328 Mo après 25 s), contrôle d'intégrité passé, fenêtre créée.

### Opérations Active Directory

**Non testées en domaine réel pendant cette session** : aucun contrôleur
`sarytany.local` n'est joignable depuis cet environnement et la consigne est de
ne déclarer réussi que ce qui a été exécuté. Ces fonctions n'ont pas été
modifiées par la refonte ; la recette domaine doit être relancée sur la VM de
labo (`docs/05-Lab-VMware.md`) avant mise en production.

---

## 7. Limites connues

- Les 11 vues héritées listées en §5 restent inaccessibles. Elles ne sont pas
  supprimées (leur logique de ViewModel est conservée) mais ne sont plus
  atteignables depuis la navigation. Une réactivation est possible si un besoin
  métier précis se présente.
- La validation visuelle repose sur un rendu hors écran : le placement réel des
  fenêtres, les survols souris et les infobulles n'ont pas pu être observés sur
  un bureau physique.
- Le facteur d'échelle est simulé par transformation de mise en page ; un
  changement de DPI système réel (panneau Windows) reste à vérifier sur une
  station.
- Chaque exécution de test a ajouté des lignes `ApplicationStarted` dans
  `audit.db` : c'est la traçabilité de l'application qui fonctionne, pas un
  effet de bord à corriger. Le compte affiché pendant les passes est passé de
  383 à 395 entrées.

---

## 8. Comment relancer la fabrication

```bat
REM 1. Sauvegarder l'exécutable courant comme version de secours
REM 2. Publier
dotnet publish frontend\ADOps.GpoManager.App\ADOps.GpoManager.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o publish
REM 3. Régénérer le manifeste d'intégrité (laissé volontairement ouvert : valider la boîte de dialogue)
publish\ADOps.GpoManager.App.exe --generate-integrity-manifest
```

`publish.bat` exécute ces trois étapes, mais l'instance en cours d'exécution
verrouille `publish\ADOps.GpoManager.App.exe` : la fermer avant de publier.
