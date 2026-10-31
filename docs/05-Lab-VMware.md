# 05 — Plateforme de test VMware (Windows Server 2019 + PC client)

Guide de mise en route pour exécuter l'application sur une plateforme de
virtualisation : **un contrôleur de domaine Windows Server 2019** et **un PC
client** joint au domaine, sur un même sous-réseau `/24`.

Ce document décrit le **minimum** à mettre en place pour que l'application
fonctionne, et les pièges propres à cet environnement.

---

## 1. Topologie de référence

| Rôle | Système | Réseau | DNS |
|---|---|---|---|
| `VM-DC01` — contrôleur de domaine AD DS | Windows Server 2019 | `192.168.10.10/24` | Intégré à AD DS (`lab.local`) |
| `VM-CLIENT01` — poste d'administration | Windows 10/11 | `192.168.10.20/24` | `192.168.10.10` |
| Application | exécutable autonome sur `VM-CLIENT01` | — | — |

Sur VMware, utilisez un réseau **host-only** ou un **LAN** dédié : un réseau NAT
partage la plage d'adresses avec la passerelle VMware et le service DHCP, ce qui
pollue la découverte du parc et complique le diagnostic DNS.

> Le réseau **NAT** fonctionne, mais la découverte de sous-réseau y remonte alors
> la passerelle VMware comme hôte. C'est attendu, pas un défaut.

---

## 2. Contrôleur de domaine (une seule fois)

Promotion du rôle AD DS, DNS intégré, puis :

```powershell
# 1. Vérifier que le DC est bien promoted et fait office de DNS du domaine
Get-ADDomain
Get-Service NTDS, DNS

# 2. Créer la première OU — un domaine fraîchement promu n'en contient aucune,
#    et le module de déploiement ne peut cibler que des OU.
New-ADOrganizationalUnit -Name "Postes" -Path "DC=lab,DC=local"
New-ADOrganizationalUnit -Name "Poste-CLIENT01" -Path "OU=Postes,DC=lab,DC=local"

# 3. Synchroniser l'heure — une dérive entre le DC et le client invalide
#    toute authentification Kerberos, sans message explicite.
w32tm /config /manualpeerlist:"time.windows.com,0x8" /sync /update
Restart-Service w32tm
```

### WinRM : optionnel en mode local

En mode d'exécution **local (RSAT)**, WinRM n'est pas nécessaire : les
commandes s'exécutent sur le poste d'administration avec le compte Windows
de la session. C'est le mode le plus fiable sur une plateforme de test.

Activez-le seulement si vous souhaitez aussi exercer le mode distant :

```powershell
# Sur le contrôleur, en administrateur (une seule fois par machine) :
Enable-PSRemoting -Force
Set-Service WinRM -StartupType Automatic
Start-Service WinRM
Get-NetFirewallRule -Group "Windows Remote Management" | Enable-NetFirewallRule
```

`Enable-PSRemoting` crée un **écouteur HTTP 5985** et un certificat HTTPS
**auto-signé**. L'application, qui refuse de contourner la validation du
certificat, basculera alors d'elle-même sur HTTP 5985 en mode « Négocier » et
le signalera (voir §5).

---

## 3. PC client

```powershell
# 1. Joindre au domaine (redémarrage demandé)
$domain = "lab.local"
$cred = Get-Credential -Message "Compte administrateur de $domain"
Add-Computer -DomainName $domain -Credential $cred -Restart

# 2. Après redémarrage : RSAT (Gestion des stratégies de groupe + AD DS)
#    PowerShell en administrateur :
Add-WindowsCapability -Online -Name Rsat.ActiveDirectory.DS-LDS.Tools~~~~0.0.1.0
Add-WindowsCapability -Online -Name Rsat.GroupPolicy.Management~~~~0.0.1.0

# 3. Vérifier — c'est ce que le Health Check contrôle à l'étape 2
Get-Module -ListAvailable GroupPolicy, ActiveDirectory | Select Name, Version
```

> **Hors ligne ?** `Add-WindowsCapability -Online` a besoin de Windows Update
> ou d'un source WSUS. Sur un réseau isolé sans WSUS, l'installation échoue.
> Un poste Windows 10/11 joint au domaine dispose souvent de RSAT en boîte de
> livraison ; dans le cas contraire, l'installation peut être fournie par le
> support d'installation ou par un serveur WSUS dédié.

---

## 4. Premier lancement

> **Correction préalable à cette version.** Le script complet de chaque
> commande était assemblé ainsi :
> `$out = ( Import-Module GroupPolicy … ; Get-GPO -All … )`.
> En PowerShell, les parenthèses regroupent **une seule expression** : tout
> corps comportant plus d'une instruction était refusé par l'analyseur
> (« Parenthèse fermante manquante »), donc **aucune** commande ne
> s'exécutait. Le corps est désormais exécuté dans un scriptblock
> `& { … }`, qui accepte une suite d'instructions. Le comportement d'un corps
> simple est inchangé.

1. Lancer l'exécutable **en tant qu'administrateur** : les liaisons de GPO
   (`New-GPLink`) et les actions de remédiation exigent l'élévation
   (`PrivilegeGate`).
2. **Aucune connexion à saisir** : l'application utilise le compte Windows de
   la session. Le poste étant joint au domaine (`lab.local`), la détection
   automatique trouve le contrôleur et le domaine.
3. **Vérification de la détection** (le health check joue au démarrage) :

   | Étape | Résultat attendu | Si elle échoue |
   |---|---|---|
   | Résolution DNS | `dc1.lab.local` résolu | DNS du client ≠ DNS du domaine |
   | Modules locaux (RSAT) | `GroupPolicy, ActiveDirectory` | RSAT non installé (§3) |
   | WinRM | avertissement ou succès | Normal si WinRM n'est pas activé |
   | Accès AD DS | domaine `lab.local` | Le poste n'est pas joint |
   | GroupPolicy | nombre de GPO | Module GPMC absent |

4. Le mode **local (RSAT)** est recommandé. Il est choisi automatiquement
   quand RSAT est présent et qu'aucun compte explicite n'a été authentifié
   à distance.
5. La **synchronisation immédiate** s'enchaîne : OU, ordinateurs, comptes
   clients, GPO et liens sont collectés et affichés dans la barre supérieure.
   Le bouton **⟳ Synchroniser** relance la collecte.

---

## 5. Le canal WinRM

Réglable dans **Paramètres ▸ Canal WinRM**, appliqué sans redémarrage.

| Réglage | Effet | Quand |
|---|---|---|
| **Négocier** (défaut) | HTTPS 5986 ; repli HTTP 5985 si et seulement si l'échec vient du canal chiffré | Plateforme de test |
| **HTTPS strict** | 5986 uniquement, aucun repli | Contrôleur de production |
| **HTTP en clair** | 5985 imposé | Réseau de test isolé |

Un repli automatique est **journalisé, audité et affiché** dans le Health Check
(étape WinRM en jaune : « Repli automatique depuis HTTPS : … »). Ce n'est
jamais silencieux.

Pour chiffrer réellement le canal sur un lab :

```powershell
# Sur le contrôleur, en administrateur :
$cert = New-SelfSignedCertificate -DnsName "dc1.lab.local" `
        -CertStoreLocation Cert:\LocalMachine\My
New-Item -Path WSMan:\LocalHost\Listener -Transport HTTPS -Address * `
         -CertificateThumbPrint $cert.Thumbprint -Force
New-NetFirewallRule -Name "WinRM 5986" -DisplayName "WinRM HTTPS" `
         -Protocol TCP -LocalPort 5986 -Action Allow
```

Un certificat auto-signé ne suffit pas : le poste d'administration doit
approuver l'autorité, sinon la validation échoue à nouveau.

---

## 6. Données locales et remise à zéro

| Élément | Emplacement |
|---|---|
| Base d'audit (SQLite) | `%LOCALAPPDATA%\ADOps\GpoManager\audit.db` |
| Configuration | `%LOCALAPPDATA%\ADOps\GpoManager\settings.json` |
| Journaux (rotation 5 × 5 Mo) | `%LOCALAPPDATA%\ADOps\GpoManager\logs\` |

**Remise à zéro complète** : fermer l'application, supprimer le dossier
`%LOCALAPPDATA%\ADOps\GpoManager`. Le schéma est recréé automatiquement au
prochain démarrage (toutes les tables sont en `CREATE TABLE IF NOT EXISTS`).

La découverte réseau balaye le **/24** du contrôleur : avec `192.168.10.10`,
elle couvre `192.168.10.1-254` — la plage attendue sur un lab.

Sur un réseau VMware **NAT**, la passerelle (souvent `.2`) répond aussi au ping
et apparaît dans le parc comme hôte découvert, sans lien avec AD DS. C'est le
comportement normal d'un balayage de sous-réseau, pas une anomalie. Pour éviter
la confusion, préférez un réseau host-only.

---

## 7. Durée de session

L'application se **verrouille après 10 minutes d'inactivité** par défaut. Sur une
plateforme de test, où l'on revient au poste après une installation, une attente
de dix minutes est pénible. Pour désactiver le verrouillage pendant une session
de travail :

```jsonc
// %LOCALAPPDATA%\ADOps\GpoManager\settings.json — application fermée
{
  "security": {
    "sessionTimeoutMinutes": 0   // 0 = jamais verrouiller
  }
}
```

Le réglage n'est pas modifiable depuis l'interface ; il s'applique au prochain
démarrage. Sur un poste de production, conserver la valeur par défaut.

---

## 8. Pièges fréquents

| Symptôme | Cause probable | Correction |
|---|---|---|
| « Impossible de trouver un contrôleur de domaine » | Poste non joint, ou DNS client qui pointe ailleurs | `Add-Computer`, puis vérifier `Get-DnsClientServerAddress` |
| Health Check bloqué sur WinRM | WinRM non activé **et** RSAT absent | Activer PSRemoting (§2) ou installer RSAT (§3) |
| Toutes les authentifications échouent | **Dérive d'horloge** entre DC et client | `w32tm /resync` sur les deux machines |
| Aucune cible de déploiement | Domaine fraîchement promu : zéro OU | `New-ADOrganizationalUnit` (§2) |
| RSoP vide sur le client | WinRM absent sur le client **ou** RSAT absent | WinRM est requis pour le RSoP, même en mode local |
| `The certificate chain was issued by an authority that is not trusted` | Écouteur 5986 auto-signé | « Négocier » (§5), ou installer une autorité de confiance |
| Mot de passe demandé mais ignoré | Mode local : les commandes utilisent le compte **Windows** de la session | Se connecter au domaine avec un compte du groupe Administrateurs |

---

## 9. Voir aussi

- [`04-ApplicationSecurity.md`](04-ApplicationSecurity.md) — transport, secrets, audit
- [`01-Architecture.md`](01-Architecture.md) —organisation du code
- [`03-SecurityHardening.md`](03-SecurityHardening.md) — référentiel de durcissement GPO