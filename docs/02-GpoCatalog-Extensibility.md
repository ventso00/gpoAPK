# ADOps GPO Manager — Catalogue de modèles & extensibilité (Étape 2)

Ce document décrit le **cadre d'extension du catalogue de modèles de
stratégies** : comment ajouter, mettre à jour ou retirer un type de GPO
sans toucher au code C#. Un « modèle » (template) est une *définition*
versionnée : il ne devient une GPO du domaine qu'après création
explicite via l'assistant de déploiement de l'application.

## 1. Principe général

```
backend/ADOps.GpoManager.Templates/
├── ADOps.GpoManager.Templates.csproj   ← <EmbeddedResource Include="Templates\*.json" />
├── TemplateCatalog.cs                   ← loader (implémente IGpoCatalogService)
└── Templates/
    ├── *.json                           ← un fichier = un modèle (clé : templateId)
    └── …
```

- Les fichiers JSON sont **embarqués dans l'assembly** : aucune
  dépendance à des fichiers externes au moment de l'exécution.
- `TemplateCatalog` charge tous les `.json` embarqués et les expose triés
  (catégorie puis nom d'affichage) via `GetTemplates()` / `GetById()`.
- La **validation des paramètres** est générique : elle est pilotée par
  la déclaration `parameters` du fichier (types `Text`, `MultilineText`,
  `Integer`, `Boolean`, `Enum`). Ajouter un modèle n'exige **aucune
  modification de code**.

## 2. Règles de chargement (comportement garanti)

| Règle | Effet |
|---|---|
| `schemaVersion` ≠ `TemplateCatalog.CurrentSchemaVersion` (actuellement 1) | Fichier **rejeté silencieusement** (jamais deviné) |
| JSON syntaxiquement invalide | Fichier **ignoré**, le catalogue reste disponible |
| `templateId` vide ou absent | Fichier ignoré |
| `templateId` en double | Le dernier fichier chargé **écrase** le premier (déterministe : ordre alphabétique des ressources) |

Ces invariants sont couverts par
`tests/ADOps.GpoManager.Tests/TemplateCatalogTests.cs` (aucun fichier
embarqué ne doit être rejeté à la construction : le test échoue si un
JSON devient invalide).

## 3. Référence du schéma (version 1)

Schéma JSON complet et validable :
[`docs/templates/template.schema.json`](templates/template.schema.json).

| Champ | Obligatoire | Type/Contrainte | Rôle |
|---|---|---|---|
| `schemaVersion` | oui | entier, `1` | Version du schéma. Autre valeur = rejet. |
| `templateId` | oui | `kebab-case`, unique | Clé primaire du catalogue. **Ne jamais réutiliser/recycler.** |
| `displayName` | oui | texte | Nom affiché dans le catalogue (français). |
| `category` | oui | texte | Regroupement d'affichage (Sécurité, Restrictions, Périphériques, Réseau, Audit…). |
| `description` | oui | texte | Quoi et pourquoi. |
| `expectedEffect` | oui | texte | Effet observable après application. |
| `scope` | oui | `Computer` ou `User` | Ruche ciblée : HKLM (ordinateur) ou HKCU (utilisateur). |
| `riskLevel` | oui | `Low`, `Medium`, `High` | Risque opérationnel affiché avant déploiement. |
| `supportedClients` | oui | tableau (≥ 1) | Ex. `["Windows 10", "Windows 11"]`. |
| `requirements` | non | tableau | Prérequis (ADMX, licence, rôle…). |
| `parameters` | oui | tableau | Paramètres de l'assistant (peut être vide). |
| `warnings` | non | tableau | Avertissements opérationnels. |
| `supportStatus` | oui | `Available`, `MissingPrerequisite`, `NotSupported`, `ComingSoon` | Seul `Available` est déployable et validé. |
| `supportNote` | si `Available` | texte | **Preuve technique** : chemin exact `HKLM\...!Valeur` ou `HKCU\...!Valeur`. |

### Paramètres

| Champ | Obligatoire | Rôle |
|---|---|---|
| `name` | oui | Clé technique (sans espace), utilisée par la validation. |
| `displayName` | oui | Libellé de l'assistant. |
| `type` | oui | `Text`, `MultilineText`, `Integer`, `Boolean`, `Enum`. |
| `description` | oui | Aide incluant valeurs acceptées + recommandation. |
| `isRequired` | oui | Un requis vide bloque la validation. |
| `defaultValue` | non | Valeur préremplie (texte). |
| `placeholder` | non | Exemple dans la zone de saisie. |
| `enumValues` | si `Enum` | Valeurs autorisées (≥ 1). Comparaison insensible à la casse. |

## 4. Ajouter un modèle — pas à pas

1. **Vérifier la stratégie Windows** : n'utiliser que des valeurs de
   stratégie **documentées** (référence ADMX officielle / `registry.pol`
   observée après configuration manuelle via `gpedit.msc`). Le chemin
   vérifié va dans `supportNote`, au format
   `HKLM\SOFTWARE\...!NomValeur=<valeur>` ou `HKCU\...`.
2. **Créer le fichier** `backend/ADOps.GpoManager.Templates/Templates/<templateId>.json`
   à partir du squelette ci-dessous (contenu en français, `<` et `>`
   échappés si nécessaire).
3. **Valider le JSON** contre `docs/templates/template.schema.json`
   (VS Code + extension JSON Schema, ou n'importe quel validateur
   draft 2020-12).
4. **Reconstruire** : `build.bat` — les JSON sont embarqués à la
   compilation (`<EmbeddedResource Include="Templates\*.json" />`),
   aucune inscription n'est nécessaire.
5. **Vérifier les tests** : `TemplateCatalogTests` échoue si un fichier
   est rejeté (schéma, JSON invalide, identifiant dupliqué, champ vide).
   Ajouter un cas de validation de paramètres si le modèle a des
   paramètres.
6. **Vérifier dans l'application** : le modèle apparaît dans le
   catalogue, filtrable par nom/catégorie ; l'assistant valide les
   paramètres et refuse un déploiement tant que la GPO n'est pas créée.

### Squelette prêt à copier

```json
{
  "schemaVersion": 1,
  "templateId": "mon-nouveau-modele",
  "displayName": "Titre affiché dans le catalogue",
  "category": "Sécurité",
  "description": "Ce que fait la stratégie et pourquoi on l'applique.",
  "expectedEffect": "Effet observable sur les postes/utilisateurs ciblés.",
  "scope": "Computer",
  "riskLevel": "Medium",
  "supportedClients": ["Windows 10", "Windows 11"],
  "requirements": [
    "Aucun prérequis ADMX (…)" 
  ],
  "parameters": [
    {
      "name": "monParametre",
      "displayName": "Libellé du paramètre",
      "type": "Enum",
      "description": "Valeurs acceptées et valeur recommandée.",
      "isRequired": true,
      "enumValues": ["Option A", "Option B"]
    }
  ],
  "warnings": [
    "Impact opérationnel éventuel et comment le réverser."
  ],
  "supportStatus": "Available",
  "supportNote": "Chemins vérifiés : HKLM\\SOFTWARE\\Microsoft\\...\\Cle!NomValeur=<valeur> (Configuration ordinateur). Rétablissement : supprimer la valeur."
}
```

### Modèle sans paramètre

Laisser `"parameters": []` : l'assistant déploie directement la GPO
sans écran de saisie (utile pour les stratégies tout-ou-rien).

## 5. Mettre à jour ou retirer un modèle

- **Modifier des valeurs** : éditer le JSON, reconstruire. Les GPO déjà
  créées sur le domaine ne sont pas affectées (le modèle n'est qu'une
  définition).
- **Changer la sémantique** (paramètres existants supprimés/renommés,
  effet différent) : **nouveau `templateId`**, ancien fichier passé en
  `supportStatus: "NotSupported"` avec un `supportNote` expliquant le
  remplacement — un `templateId` ne change jamais de signification.
- **Retirer un modèle** : passer en `NotSupported` (l'historique
  d'audit reste lisible) ; supprimer le fichier seulement si aucun
  déploiement n'y fait référence.
- **Migration de schéma** : si la version 2 voit le jour, le loader
  devra accepter explicitement `schemaVersion: 2` et mapper les champs ;
  la version 1 reste chargeable tant que `CurrentSchemaVersion` le
  permet.

## 6. Bonnes pratiques de contenu

1. **Un seul réglage par modèle** (ou un réglage cohérent
   multifacettes, comme le verrouillage écran de veille) : facilite
   l'analyse de risque et le ciblage par OU.
2. **`supportNote` = preuve** : chemin de registre exact avec le `!`
   séparateur cle/valeur et la valeur concrète. C'est ce qui distingue
   un modèle vérifié d'une intention.
3. **Pas de valeur de registre « découverte maison »** : uniquement des
   clés de stratégie (`Policies`) documentées par Microsoft ; les
   stratégies non représentables en valeur de registre (droits
   utilisateur, stratégie d'audit avancée, mot de passe) ne sont pas
   modélisées ici — voir `docs/03-SecurityHardening.md`.
4. **Avertissements opérationnels réels** : ce que perd l'utilisateur,
   comment réverser (supprimer la valeur de registre / déliker la GPO).
5. **Risque `High`** réservé aux modèles pouvant bloquer
   l'administration à distance ou l'accès aux postes (PowerShell, NTLM,
   RDP).
6. **Exclure les administrateurs** : les restrictions utilisateur
   (Panneau de configuration, shell, cmd) doivent être liées aux OU
   d'utilisateurs standard, jamais aux comptes d'administration —
   l'exclure dans `warnings`.

## 7. Liste des modèles embarqués (état actuel)

| templateId | Catégorie | Portée | Réglage principal |
|---|---|---|---|
| `control-panel-settings` | Restrictions | User | **Règle obligatoire** : blocage Panneau de configuration + Paramètres (`NoControlPanel`) |
| `shell-restrictions` | Restrictions | User | Exécuter, touches Win, arrêt, verrouillage de session |
| `cmd-registry-block` | Restrictions | User | cmd.exe (scripts inclus ou non) + regedit/regedt32 |
| `powershell-execution-policy` | Restrictions | Computer | EnableScripts + ExecutionPolicy (Désactivé → Unrestricted) |
| `uac-hardening` | Sécurité | Computer | Bureau sécurisé, comportement des utilisateurs standards |
| `smartscreen-enforcement` | Sécurité | Computer | SmartScreen Explorer (Warn/Block) |
| `asr-exploit-guard` | Sécurité | Computer | Règles ASR Defender (GUID, mode audit/blocage) |
| `legacy-protocols-hardening` | Réseau | Computer | SMB1, LLMNR, NTLMv2 uniquement |
| `rdp-lockdown` | Réseau | Computer | RDP désactivé ou NLA obligatoire |
| `screensaver-lock` | Sécurité | User | Écran de veille + verrouillage par mot de passe |
| `autorun-hardening` | Périphériques | Computer | Autorun/AutoPlay désactivés tous lecteurs |
| `removable-storage-restrictions` | Périphériques | Computer | Accès USB/SD (lecture seule / bloqué) |
| `windows-update` | Mise à jour | Computer | Report/paramètres Windows Update |
| `windows-defender-firewall` | Sécurité | Computer | Pare-feu Defender |
| `browser-url-blocklist` | Internet | Computer | Liste de sites interdits |
| `audit-policy-subcategory` | Audit | Computer | Refuser la stratégie d'audit héritée (préambule `auditpol`) |

Le détail complet du référentiel de durcissement (droits utilisateur,
stratégie de mot de passe, audit avancé, modèle d'OU) est dans
[`docs/03-SecurityHardening.md`](03-SecurityHardening.md).
