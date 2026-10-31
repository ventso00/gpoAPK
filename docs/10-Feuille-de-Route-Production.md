# Feuille de route — mise en service sur un vrai domaine Active Directory

Documentation d'exploitation : comment passer de « l'exe est téléchargé » à « les GPO sont
déployées et contrôlées en production », par phases avec un point de contrôle à chaque étape.

Principe directeur : **jamais de GPO liée à `Default-First-Site-Name`, à la racine du domaine ou à
une OU de production directement**. On monte en puissance : labo → périmètre pilote → service →
domaine entier, avec rollback possible à chaque vague.

---

## Phase 0 — Décision et cadrage (½ journée)

| Question | Repère |
| --- | --- |
| Qui manipule l'outil ? | Comptes **admin du domaine** ou délégation GPO ; jamais un compte de production partagé. |
| Quel canal d'exécution ? | **Local (RSAT)** = recommandé, sans ouverture de port. **Distant (WinRM)** = si le poste d'admin n'est pas joint au domaine. |
| Quel périmètre de 1ᵉʳ déploiement ? | 1 OU pilote (ex. `OU=Tests`) avec 5 à 20 objets, dont 2 ordinateurs et 2 utilisateurs réels. |
| Quelle fenêtre de validation côté client ? | `gpupdate /force` manuel possible, mais prévoir le cycle naturel (90 min ±30 min utilisateurs, 15 min ordinateurs, see Phase 5). |

**Livrable** : une page « périmètre + comptes + canal + date de vague 1 ».

---

## Phase 1 — Poste d'administration (30 min)

1. Windows 10/11 ou serveur joint au domaine (ou joignable), **horloge synchronisée sur le DC**
   (Kerberos tolère 5 min d'écart — au-delà, l'authentification échoue sans message clair).
2. Installer RSAT :
   ```powershell
   Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0
   Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0
   Get-Module -ListAvailable GPO,AD,GroupPolicy
   ```
3. Vérifier la résolution DNS du contrôleur **par FQDN** :
   ```powershell
   Resolve-DnsName dc1.mon-domaine.local -Type A
   nltest /dsgetdc:mon-domaine.local
   ```
4. Poser l'application : dézipper `ADOps-GPO-Manager-livre.zip`, lancer
   `publish\Lancer-Application.cmd` **en tant qu'administrateur** (les cmdlets GPO exigent
   l'élévation).
5. Contrôle d'intégrité : l'appli vérifie `integrity.manifest` au démarrage.
   Manuel : `Get-FileHash .\ADOps.GpoManager.App.exe -Algorithm SHA256`.

**Critère de sortie** : `Get-GPO -Domain mon-domaine.local -All` répond en PowerShell pur.

### Si le canal distant est nécessaire

Sur le DC (une seule fois, en admin) :

```powershell
Enable-PSRemoting -Force                          # listener HTTP 5985
winrm qc -force                                   # alternative minimale
Set-Item WSMan:\localhost\Client\TrustedHosts "dc1.mon-domaine.local" -Concatenate -Force   # si non joint au domaine
```

HTTPS réel (5986) : certificat sur le DC + `winrm set winrm/config/service @{AllowUnencrypted="false"}`,
puis choisir **HTTPS strict** dans *Paramètres ▸ Canal WinRM*. Le repli HTTP 5985 transporte les
données en clair : à réserver au labo isolé.

Pare-feu : 5985/TCP ou 5986/TCP depuis le poste d'administration uniquement (filtre de source).

**Ports utiles au total** : 53 (DNS), 389/636 (LDAP/LDAPS), 88 (Kerberos), 445 (SMB — SYSVOL pour
les scripts GPO), 5985/5986 (WinRM), 135 + ports dynamiques (RPC, mode local).

---

## Phase 2 — Connexion et diagnostic dans l'application (10 min)

Écran de connexion (plein écran) : FQDN du DC, domaine, NetBIOS, compte, mot de passe.
Le bouton de connexion exécute le **diagnostic en 6 étapes** :

| # | Étape | Ce qu'elle prouve |
| --- | --- | --- |
| 1 | Résolution DNS | Le nom du contrôleur est joignable (le plus fréquent des échecs). |
| 2 | Modules RSAT | `GroupPolicy` / `ActiveDirectory` disponibles. |
| 3 | WinRM (`Test-WSMan`) | Canal distant vivant (sinon *Ignoré* en mode local). |
| 4 | Authentification | Le compte est accepté (Kerberos local, NTLM/WinRM distant). |
| 5 | `Get-ADDomain` | Lecture d'annuaire effective. |
| 6 | `Get-GPO -All` | Lecture des stratégies effectives. |

Une étape en **Échec** bloque ; **Attention** est un avertissement non bloquant. Si aucune étape
ne passe, lire le résumé final : il nomme le correctif (installer RSAT **ou** `Enable-PSRemoting`).
Les messages d'erreur sont interprétés en français comme en anglais.

**Critère de sortie** : les 6 étapes vertes (étape 3 *Ignorée* en mode local = normal), et le
tableau de bord affiche le domaine réel.

---

## Phase 3 — Inventaire et cartographie (1 h)

1. **Synchronisation de l'inventaire** : OU, utilisateurs, ordinateurs. Vérifier le nombre
   d'objets contre `Search-ADAccount` / `Get-ADOrganizationalUnit`.
2. **Découverte réseau** (facultatif) : postes joignables, compléments d'inventaire.
3. **Mapping des départements** : associer chaque service à une OU. C'est la base du ciblage ;
   fait ici une fois, il évite de choisir des DN « au feeling » à chaque déploiement.
4. **Liste des GPO existantes** : prendre la photographie de l'état initial (exporter la liste).
   Utile pour prouver, plus tard, que rien d'inespéré n'a été modifié.

> Les OU de production sensibles (`Domain Controllers`, OU contenant des comptes à privilèges)
> doivent être identifiées ici et **exclues** du périmètre de déploiement de l'outil.

**Critère de sortie** : inventaire stable après resynchronisation, mapping complet, photo de
l'état initial archivée.

---

## Phase 4 : déployer la première GPO (vague 1 — périmètre pilote)

Toujours dans l'ordre : **créer → lier sur l'OU pilote → valider sur un client → élargir.**

1. **Création depuis le catalogue** : choisir un modèle, vérifier le script PowerShell embarqué
   (bouton d'aperçu), créer la GPO. La création est **auditée** (action `GpoCreate`).
2. **Ciblage de sécurité** : dans la GPO créée, ne garder que le groupe cible en
   *Authentification / Application* — la GPO ne doit **pas** s'appliquer à `Domain Computers`
   si elle cible des utilisateurs. Ce point est la cause n°1 d'effets de bord.
3. **Liaison** : lier la GPO à l'OU pilote uniquement (`New-GPLink`, audité `GpoLink`).
   Laisser **Enforced** désactivé sauf nécessité, et **Link Enabled** à *vrai*.
4. **Validation client** sur 2 postes :
   ```powershell
   gpupdate /force
   gpresult /r                       # la GPO apparaît-elle dans les paramètres appliqués ?
   gpresult /h C:\temp\rsop.html     # rapport RSoP complet
   ```
   Dans l'application : **RSoP par ordinateur** pour le même poste — les deux doivent concorder.
5. **Vérifier l'effet réel** (regkey, droit, paramètre) et non seulement le rapport.

**Critère de sortie** : 2 postes + 2 utilisateurs conformes, zéro poste hors périmètre touché,
journaux d'audit de l'application cohérents.

---

## Phase 5 — Élargissement par vagues

| Gate | Périmètre | Vérification avant de passer à la suite |
| --- | --- | --- |
| V1 | 1 OU pilote (5–20 objets) | RSoP conforme, aucune régression signalée. |
| V2 | 1 service complet (~100 postes) | Inventaire de conformité par échantillonnage (10 %). |
| V3 | Site / département | Mesure du temps d'application, impacts ouverture de session. |
| V4 | Domaine (hors OU exclues) | Validation du responsable d'exploitation. |

Règles d'exécution :

- **Réplication** : après une modification de GPO, attendre la réplication AD et la propagation
  SYSVOL (vérifier `Get-ADReplicationPartnerMetadata` / le versionnage de la GPO).
- **Fenêtre de changement** : déployer hors heures d'ouverture de session massives ; les
  stratégies d'ordinateur s'appliquent au démarrage.
- **Rollback** : délier (`Remove-GPLink`, audité `GpoUnlink`) **avant** de supprimer. Une GPO liée
  mais désactivée (`Link Enabled = false`) est le point d'arrêt le plus sûr et le plus rapide.
- **Ne jamais** lier une GPO de test à la racine du domaine ni `Enforced` sur une OU parente.

---

## Phase 6 — Exploitation courante

- **Diagnostic & réparation** : moteur de diagnostic → plan de remédiation → application **après
  validation explicite** (audit `RemediationApply`). À utiliser sur les dérives individuelles, pas
  comme mechanism de déploiement de masse.
- **Rapports** : production régulière de l'état des GPO et de la conformité par OU ; c'est la
  trace demandée en audit.
- **Journal d'audit** : consulter `AuditEntry` (action, serveur cible, résultat, utilisateur,
  identifiant d'opération). À conserver : c'est la preuve de qui a lié quoi et quand.
- **Rotation des comptes** : l'application ne stocke aucun mot de passe (`SecureString` uniquement) ;
  le renouvellement se fait donc au niveau des comptes AD, comme pour n'importe quel outil d'admin.

---

## Phase 7 — Durcissement de l'outil lui-même

1. **Signature du binaire** : `signtool` / certificate EV, ou au minimum distribution depuis un
   partage interne de confiance (SmartScreen disparaît, et l'exclusion antivirus n'est plus nécessaire).
2. **Distribution** : copie depuis `\\serveur\logiciels\` plutôt que GitHub si les postes sont
   sensibles ; l'exécutable est autonome (pas d'install .NET requis).
3. **Restriction d'usage** : groupes de sécurité restreints sur les OU de production ; l'outil ne
   remplace pas la délégation AD, il l'utilise.
4. **Sauvegarde des modèles** : le catalogue JSON est versionné dans le dépôt — toute évolution
   d'un modèle passe par une revue de code.
5. **Mise à jour de l'application** : remplacer l'exe + `integrity.manifest` ensemble ; le contrôle
   d'intégrité au démarrage refusera silencieusement un mélange de versions.

---

## Checklist avant mise en production

- [ ] RSAT installé **ou** WinRM HTTPS (5986) configuré sur les DC ciblés
- [ ] Horloge du poste d'administration synchronisée sur le DC
- [ ] Compte dédié d'administration, pas de compte de service partagé
- [ ] Diagnostic de connexion : 6 étapes au vert (3 = *Ignorée* acceptable en mode local)
- [ ] OU de production exclues listées et connues
- [ ] Photographie de l'état initial des GPO archivée
- [ ] 1 GPO déployée en vague pilote et validée par `gpresult` sur client
- [ ] Politique de rollback connue (délier → désactiver → supprimer, dans cet ordre)
- [ ] Audit de l'application consultable et conservé
- [ ] Binaire signé ou canalisé par un dépôt interne

---

## En cas d'échec de connexion

| Symptôme | Cause probable | Action |
| --- | --- | --- |
| DNS en échec, tout le reste ignoré | FQDN incorrect ou poste hors du domaine DNS | Utiliser le FQDN exact du DC ; vérifier `nslookup` |
| WinRM en échec | `Enable-PSRemoting` jamais exécuté, ou pare-feu | Sur le DC : `Enable-PSRemoting -Force` ; ouvrir 5985/5986 |
| Authentification en échec, DNS/modules OK | Mot de passe, compte verrouillé, ou horloge désynchronisée | `klist purge`, resynchroniser l'heure, retester en LDAP simple |
| `Get-ADDomain` en échec | Compte sans droit de lecture | Vérifier l'appartenance à *Read-only Domain Controllers* ou *DAS* |
| `Get-GPO` en échec, AD OK | Module `GroupPolicy` absent | Installer `Rsat.GroupPolicy.Management` |
| « Aucun canal d'exécution » | Ni RSAT ni WinRM | Choisir l'un des deux (Phase 1) |

Voir aussi [`03-SecurityHardening.md`](03-SecurityHardening.md) pour le référentiel de durcissement
et [`09-Sync-et-Application-Reelle.md`](09-Sync-et-Application-Reelle.md) pour le détail des flux.
