# ADOps GPO Manager — Sécurité applicative (durcissement)

> **Périmètre** : ce document couvre la sécurité de l'**application**
> (identifiants, sessions, transport, journaux, intégrité des binaires).
> Le durcissement du **parc** (GPO CIS/ANSSI déployées par l'application)
> est dans [`docs/03-SecurityHardening.md`](03-SecurityHardening.md).
> Les deux sont complémentaires et sans recouvrement.

## 1. Identifiants et authentification

| Mesure | Implémentation | Fichier |
|---|---|---|
| Aucun stockage en clair | Mot de passe jamais écrit dans `settings.json`, SQLite ou les journaux. Seuls le FQDN, le domaine, le NetBIOS, le mode et les options de sécurité sont persistés. | `Configuration/AppConfiguration.cs` |
| SecureString de bout en bout | Saisie (PasswordBox) → `SecureString` → copie verrouillée → effacement (`Dispose`) | `Controls/PasswordBoxAssistant.cs`, `Models/ConnectionCredentials.cs` |
| Transport du secret | Blob **DPAPI (portée utilisateur courant)** injecté sur **stdin** du processus PowerShell ; jamais en argument de ligne de commande, jamais sur disque | `PowerShell/RemoteExecutor.cs`, `Models/RemoteCredentialHandoff.cs` |
| Coffre Windows (optionnel) | Mémorisation du compte admin dans le **Credential Manager** (chiffré par l'OS, lié au profil) | `Core/Security/WindowsCredentialVault.cs` |
| Formats acceptés | `DOMAINE\utilisateur`, UPN `utilisateur@domaine`, compte Windows courant (Kerberos) — validés avant usage | `Validation/UserValidator.cs` |
| Rédaction | Tout journal / audit passe par le rédacteur (mots de passe, blobs base64, clés hexa, e-mails) | `Core/Security/LogSanitizer.cs` |

### Vérification rapide

```powershell
# Aucun secret dans les fichiers de configuration
Select-String -Path "$env:LOCALAPPDATA\ADOps\GpoManager\settings.json" -Pattern "password|secret|token"  # -> aucun résultat
```

## 2. Sessions et moindre privilège

- **Verrouillage automatique** après inactivité (défaut **10 minutes**,
  réglable, `0` = désactivé) : la fenêtre principale est masquée et une
  ré-authentification complète est exigée (`Core/Security/SessionGuard.cs`,
  fil dans `Views/MainWindow.xaml`).
- **Bouton « Verrouiller »** dans la barre d'état (verrouillage manuel immédiat).
- **Contrôle de privilège avant action sensible** : liaison/suppression de GPO
  et corrections système vérifient l'élévation **avant** exécution
  (`Core/Security/PrivilegeGate.cs`). Une session non élevée reçoit un refus
  explicite plutôt qu'un échec tardif.

## 3. Transport et communications

| Exigence | Comportement |
|---|---|
| **HTTPS prioritaire** | WinRM en `-UseSSL` sur le port **5986** par défaut (`WinRmTransport = Auto`) |
| **Validation du certificat** | `New-PSSessionOption -SkipCACheck:$false -SkipCNCheck:$false -SkipRevocationCheck:$false` — autorité, nom du serveur et révocation sont vérifiés |
| **Repli conditionnel et tracé** | Le repli HTTP 5985 n'est tenté **que** si l'échec est imputable au canal chiffré (écouteur 5986 absent, certificat non de confiance, connexion refusée sur ce port). Il est journalisé en `Warning`, audité et affiché dans le Health Check. Un échec métier (droits, commande refusée, GPO introuvable) n'est **jamais** rejoué en clair |
| **Repli désactivable** | `WinRmTransport = Https` interdit tout repli ; `WinRmTransport = Http` impose le clair sans tenter HTTPS |
| **Pas de contournement** | Aucune écriture automatique dans `TrustedHosts`, aucun désassemblage de la vérification SSL |
| **Diagnostic** | Le Health Check sonde le canal configuré : en `Auto`, `Test-WSMan -UseSSL -Port 5986` puis, si le premier échoue, `Test-WSMan -Port 5985` |

### Les trois canaux

| Valeur (`WinRmTransport`) | Ports | Quand l'utiliser |
|---|---|---|
| `Auto` (défaut) | 5986 puis 5985 | Plateforme de test VMware : le contrôleur n'a souvent qu'un écouteur HTTP et un certificat auto-signé. Le repli est signalé. |
| `Https` | 5986 | Contrôleur de production doté d'un certificat émis par une autorité de confiance. |
| `Http` | 5985 | Réseau de test isolé où HTTPS est impossible. Le canal n'est pas chiffré. |

> **Note de sécurité.** Le comportement par défaut a changé : une installation
> antérieure exigeait `RequireWinRmHttps = true` (HTTPS strict, sans repli) et
> échouait sur un contrôleur de test non configuré. `Auto` tente toujours HTTPS
> avec validation complète du certificat et **ne renonce au chiffrement que sur
> échec de canal avéré**. Si ce compromis ne convient pas, réglez
> `WinRmTransport = Https` — le réglage est disponible dans
> **Paramètres ▸ Canal WinRM**, appliqué sans redémarrage.
>
> L'ancien option `RequireWinRmHttps` reste lue pour compatibilité : si elle vaut
> `false`, le canal HTTP 5985 est imposé (comportement historique). Elle n'est
> plus écrite en contradiction avec `WinRmTransport`.

Si le contrôleur n'a pas d'écouteur HTTPS (prérequis côté serveur) :

```powershell
# Sur le contrôleur, en administrateur (une seule fois) :
$cert = New-SelfSignedCertificate -DnsName "dc1.sarytany.local" -CertStoreLocation Cert:\LocalMachine\My
New-Item -Path WSMan:\LocalHost\Listener -Transport HTTPS -Address * -CertificateThumbPrint $cert.Thumbprint -Force
New-NetFirewallRule -Name "WinRM 5986" -DisplayName "WinRM HTTPS" -Protocol TCP -LocalPort 5986 -Action Allow
```

### Cible Kerberos : le FQDN est obligatoire

Kerberos s'appuie sur les SPN (service principal name) enregistrés dans AD,
qui sont attachés à des **noms DNS**, jamais à des adresses IP : toute cible
IP échoue avec « L'authentification Kerberos ne peut pas être utilisée lorsque
la destination est une adresse IP ».

- Saisir le **FQDN du contrôleur** (ex. : `VMST-DC01.sarytany.local`) plutôt
  que son adresse IP ;
- l'application convertit **automatiquement** une IP saisie en FQDN via la
  localisation Netlogon (`Core/Network/DcLocator.cs`) lorsqu'elle correspond au
  contrôleur du domaine, et affiche un message explicite avec le FQDN
  recommandé sinon (fonctionne même sans zone DNS inverse PTR).

## 4. Anti-injection

- **Aucune saisie libre de script** : toutes les commandes PowerShell sont
  typées (classes dédiées), construites à partir de valeurs validées.
- **Double barrière** : `DomainValidator` / `DistinguishedNameValidator` /
  `UserValidator` valident le format, puis
  `Core/Security/InputSanitizer.cs` **ré-échappe** au point d'injection
  (guillemets simples doublés, refus des caractères de contrôle, `` ` ``, NUL,
  guillemets doubles).
- **Injection LDAP** : `EscapeLdapFilterValue` neutralise `\ * ( )` et NUL
  (RFC 4515) pour tout filtre LDAP futur.
- **Exécution** : scripts passés en `-EncodedCommand` (Base64 UTF-16) à
  `powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass`.

## 5. Journaux et piste d'audit

- **Rédaction systématique** avant écriture (messages **et** exceptions) :
  `password=… → password=[REDIGÉ]`, blobs base64, clés hexadécimales, e-mails.
- **Chaîne d'intégrité** : chaque ligne porte l'empreinte SHA-256 tronquée de
  la ligne précédente → toute suppression/insertion/modification est
  détectable. Vérification :

  ```powershell
  # Depuis l'application (référence Infrastructure) ou un script de contrôle :
  [ADOps.GpoManager.Infrastructure.Logging.FileLogger]::VerifyIntegrity("$env:LOCALAPPDATA\ADOps\GpoManager\logs\adops.log")
  # -> $null = intègre ; sinon le numéro de la première ligne altérée
  ```

- **ACL restrictive** sur le fichier courant : héritage supprimé, accès limité
  à l'utilisateur courant, SYSTEM et Administrateurs.
- **Audit SQLite** : chaque contrôle (`DiagnosticRun`) et chaque correction
  (`RemediationPlan`, `RemediationApply`) sont tracés avec auteur, horodatage
  UTC, cible, résultat, durée et identifiant d'opération.

## 6. Intégrité des binaires (anti-altération)

- À la publication, `publish.bat` génère `integrity.manifest` (SHA-256 par
  fichier : exe, DLL, runtimeconfig).
- Au démarrage, l'application recalcule les empreintes ; tout écart est
  journalisé en `Critical` et signalé par une alerte.
- Régénération manuelle : `ADOps.GpoManager.App.exe --generate-integrity-manifest`

**Détection ≠ prévention.** Pour la protection contre l'analyse statique et
l'extraction de logique, appliquer en complément (hors build, sur le binaire
publié) :

| Outil | Usage |
|---|---|
| **Dotfuscator Community** (inclus à VS) | Renommage, contrôle de flux, chiffrement de chaînes |
| **ConfuserEx / ConfuserEx2** | Obfuscation open source (contrôle de flux, anti-débogage, anti-tamper) |
| **ILMerge / single-file** | Déjà appliqué (`PublishSingleFile`) |
| Publication Release | `-p:DebugType=none -p:DebugSymbols=false` —évite de livrer les PDB |

## 7. Configuration (settings.json)

```json
{
  "Security": {
    "WinRmTransport": 2,
    "RequireWinRmHttps": true,
    "SessionTimeoutMinutes": 10,
    "LogSanitizationEnabled": true,
    "LogIntegrityProtection": true,
    "VerifyAssemblyIntegrity": true,
    "AllowCredentialVault": true
  }
}
```

`WinRmTransport` est sérialisé comme un entier : `0` = HTTPS strict,
`1` = HTTP en clair, `2` = négociation (défaut). Préférez l'écran
**Paramètres ▸ Canal WinRM**, qui écrit également `RequireWinRmHttps`
de façon cohérente avec le canal choisi.

Toutes les valeurs sont **strictes par défaut**, à une exception documentée :
`WinRmTransport = 2` autorise un repli conditionnel et tracé vers HTTP 5985,
uniquement lorsque le canal chiffré est lui-même en cause. Un repli vers le
canal HTTP (`WinRmTransport = 1`) est un choix explicite, journalisé en
avertissement, audité et signalé par le diagnostic.

## 8. Références

- ANSSI — hygiène informatique (gestion des comptes, sessions, journaux)
- NIST SP 800-53 — AC (contrôle d'accès), AU (audit), SC (canaux protégés), SI (intégrité)
- CIS Benchmarks — Windows 11 Enterprise / Server 2022
- Microsoft Learn — WinRM over HTTPS, PSSessionOption, DPAPI, Credential Manager
