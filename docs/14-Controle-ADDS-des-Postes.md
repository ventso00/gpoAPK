# Contrôle AD DS des postes clients

La console **« Console du poste & test réel »** donne à la session AD DS la
maîtrise du cycle de vie des ordinateurs du domaine : activation du compte,
déplacement d'OU (donc des GPO héritées), redémarrage et extinction, en plus du
déploiement et de la vérification des stratégies.

> **Périmètre** : cette page pilote des objets AD DS et, pour l'alimentation,
> le poste lui-même via WinRM. Elle ne fournit pas de shell distant, de
> transfert de fichiers ni de prise de contrôle graphique. Les serveurs et le
> contrôleur de domaine sont explicitement protégés.

---

## 1. Accès et sélection

1. Ouvrir **« Console du poste & test réel »** dans la barre latérale.
2. Choisir un poste client dans les cartes `CLIENT1` / `CLIENT2` (les
   contrôleurs de domaine et les systèmes serveur sont exclus de la liste).
3. La fiche affiche l'identité AD, l'adresse, le système, l'OU, l'état du
   compte et la dernière connexion connue.

Le panneau **« CONTRÔLE AD DS · CYCLE DE VIE DU POSTE »** propose alors :

| Action | Effet réel | Commande / canal |
|---|---|---|
| **Désactiver le compte** / **Activer le compte** | Change l'état `Enabled` du compte d'ordinateur dans AD DS | `Get-ADComputer`, `Disable-ADAccount` / `Enable-ADAccount` |
| **Déplacer vers l'OU** | Change l'OU de l'objet ordinateur ; les GPO héritées changent au prochain cycle | `Get-ADOrganizationalUnit`, `Move-ADObject` |
| **Redémarrer** | Redémarre le poste après 15 s | `shutdown.exe /r /t 15 /f /d p:4:1` via WinRM |
| **Éteindre** | Éteint le poste après 15 s | `shutdown.exe /s /t 15 /f /d p:4:1` via WinRM |

Les actions de répertoire passent par l'exécuteur **local (RSAT)** ou **distant
(WinRM)** selon le mode de session. Les actions de puissance passent toujours
par WinRM **sur le poste cible**, jamais par le contrôleur.

---

## 2. Confirmations et garde-fous

Chaque action est précédée d'une confirmation explicite indiquant la cible et
la conséquence. Les garde-fous suivants sont appliqués **côté service**, pas
seulement dans l'interface :

- **Serveurs et DC exclus** : un poste dont le système contient `Server`, dont
  le DN est dans `OU=Domain Controllers`, ou qui correspond au contrôleur ou à
  l'hôte local ne peut être ni désactivé, ni déplacé, ni redémarré/éteint.
- **OU Domain Controllers interdite** comme cible de déplacement : elle ne
  figure pas dans la liste et le service refuse aussi un DN ciblant cette OU.
- **Activation autorisée** sur un serveur (pour rétablir un compte), mais jamais
  la désactivation.
- **Compte désactivé** : le redémarrage/l'extinction est refusé tant que le
  compte n'est pas réactivé (le poste ne peut plus s'authentifier).
- **WinRM requis** pour les actions de puissance, `gpupdate` et `gpresult` ;
  un poste seulement joignable en DNS ne peut pas être piloté à distance.
- **Aucun mot de passe stocké** : la session Windows courante est l'identité ;
  le cas échéant, le transport distant utilise le handoff DPAPI existant.
- **Audit systématique** : succès, échec, annulation et refus de garde-fou sont
  consignés dans `audit.db` (action `ComputerControl`) et dans
  `logs\adops.log`, avec serveur, domaine, cible, durée et identifiant
  d'opération.

Après une activation/désactivation ou un déplacement, la console relance une
synchronisation d'inventaire et recharge la fiche. Après un redémarrage ou une
extinction, elle affiche le résultat et l'état de la session sans tenter de
relancer immédiatement le poste.

---

## 3. Conséquences opérationnelles

### Désactivation du compte

- Le poste ne peut plus s'authentifier au domaine, donc plus recevoir de
  stratégie ni accéder aux ressources authentifiées.
- Les sessions déjà ouvertes peuvent continuer localement jusqu'à leur
  expiration ; la désactivation n'éteint pas la machine.
- Pour couper immédiatement l'accès, désactiver le compte **puis** éteindre le
  poste (si WinRM est disponible).

### Déplacement d'OU

- Les GPO liées à l'ancienne OU cessent de s'appliquer (hors héritage commun du
  domaine) ; celles de la nouvelle OU s'appliquent au prochain `gpupdate`.
- Les filtres de sécurité existants restent attachés à la GPO, pas au poste.
- Le déplacement ne modifie ni les groupes, ni les ACL de l'objet.
- L'OU cible doit exister et être une vraie OU (`OU=…`) ; le conteneur
  `CN=Computers` n'est pas une cible acceptée.

### Redémarrage / extinction

- Un délai de 15 secondes laisse le temps d'enregistrer le travail ; le
  redémarrage est forcé (`/f`).
- Le motif enregistré est `p:4:1` (arrêt planifié par l'application).
- La session WinRM se ferme pendant l'opération : un échec de retour n'est pas
  synonyme d'échec de l'arrêt. Vérifier l'état du poste après quelques
  secondes.

---

## 4. Prérequis de lab et de production

| Besoin | Pour quoi |
|---|---|
| RSAT `ActiveDirectory` + `GroupPolicy` sur le poste d'administration, ou WinRM sur le DC | Actions de répertoire (compte, OU) |
| WinRM activé sur chaque poste client (`Enable-PSRemoting -Force`) | Alimentation, `gpupdate /force`, `gpresult /r` |
| Session élevée (administrateur du domaine ou délégation adaptée) | Écriture AD DS et ouverture des canaux distants |
| Pare-feu : 5985/5986 vers les postes | WinRM |
| DNS correct pour les postes | Résolution des noms ; un poste « DNS seul » n'a pas WinRM |

Sur le domaine de recette `sarytany.local`, les deux postes `CLIENT1` et
`CLIENT2` sont actuellement **résolus en DNS sans WinRM** : le déploiement de
GPO et les actions AD fonctionnent, mais `gpupdate`, `gpresult`, le redémarrage
et l'extinction distants échouent avec un message explicite. C'est une limite
d'environnement, pas un défaut du service.

---

## 5. Journalisation et vérification

- **Journal d'audit** : page « Journal d'audit » ou
  `%LOCALAPPDATA%\ADOps\GpoManager\audit.db`.
- **Journal fichier** : `%LOCALAPPDATA%\ADOps\GpoManager\logs\adops.log`.
- **Recherche** : filtrer la page Audit sur l'action `ComputerControl` pour
  retrouver une activation, un déplacement ou une action de puissance.
- **Preuve AD** : vérifier dans ADUC (`dssite.msc`, `dsa.msc`) ou avec
  `Get-ADComputer -Identity <DN> -Properties Enabled,DistinguishedName`.

---

## 6. Recette sans effet de bord

La procédure de validation utilisée pendant la refonte :

1. Ouvrir la console d'un poste client.
2. Cliquer sur **Désactiver le compte**, vérifier la confirmation (cible, effet,
   audit), puis cliquer **Non** : le compte doit rester activé et aucune entrée
   `ComputerControl` de succès ne doit apparaître.
3. Vérifier que l'OU `Domain Controllers` est absente de la liste de
   déplacement et que le bouton **Déplacer** reste désactivé tant qu'aucune OU
   cible n'est choisie.
4. Sur un poste de test uniquement : déplacer vers une OU de test, vérifier le
   nouveau DN, puis remettre dans l'OU d'origine.
5. Sur un poste de test WinRM actif : redémarrer, vérifier l'audit et le retour
   du poste ; ne jamais exécuter cette étape sur le DC ou un serveur.

**Rollback** :

- Compte désactivé → **Activer le compte** (même panneau).
- OU modifiée → **Déplacer vers l'OU** d'origine (le DN d'origine est visible
  dans l'audit et dans la fiche avant l'opération).
- GPO appliquée → page Déploiements, détachement par poste ou retrait du lien
  par un administrateur.
