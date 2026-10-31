using System.Security;
using ADOps.GpoManager.Core.Commands;
using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Security;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Core.Validation;
using ADOps.GpoManager.PowerShell.Host;

namespace ADOps.GpoManager.PowerShell;

/// <summary>
/// Exécute les commandes PowerShell sur une machine distante via WinRM (Invoke-Command).
/// - Compte Windows actuel : aucun credential n'est transmis (Kerberos).
/// - Compte explicite : le mot de passe transite par l'entrée standard du processus
///   sous forme d'un blob DPAPI (portée utilisateur courant) — jamais en argument de
///   ligne de commande, jamais dans les logs, jamais sur disque.
/// </summary>
public sealed class RemoteExecutor : IPowerShellExecutor
{
    /// <summary>Port WinRM HTTPS (RFC : 5986).</summary>
    public const int WinRmHttpsPort = WinRmPorts.Https;

    /// <summary>Port WinRM HTTP (RFC : 5985).</summary>
    public const int WinRmHttpPort = WinRmPorts.Http;

    /// <summary>
    /// Marqueurs d'erreur imputables au canal chiffré (absence d'écouteur 5986,
    /// certificat auto-signé ou non de confiance). Leur présence seule autorise le
    /// repli HTTP du mode <see cref="WinRmTransportMode.Auto"/> : un échec métier
    /// (commande refusée, GPO introuvable, droits insuffisants) ne doit jamais
    /// être rejoué en clair.
    /// </summary>
    internal static readonly string[] HttpsFallbackMarkers =
    {
        "the certificate chain was issued by an authority that is not trusted",
        "the certificate is not trusted",
        "untrusted certificate",
        "certificate chain",
        "the certificate has expired",
        "could not use ssl",
        "ssl connection",
        "secure channel",
        "0x80090312", // SEC_E_UNTRUSTED_ROOT
        "0x80090308", // SEC_E_INVALID_DIGEST / certificat expiré
        "0x80072ee7", // connexion refusée sur le port 5986
        "0x80072ee2", // délai dépassé ou port fermé
        "connection refused",
        "unable to connect to the remote server",
    };

    private readonly string _targetFqdn;
    private readonly ConnectionCredentials? _credentials;
    private readonly WinRmTransportMode _transport;
    private readonly ILogger? _logger;
    private readonly WinRmConnectivityProbe _connectivityProbe = new();

    public ExecutorMode Mode => ExecutorMode.Remote;

    /// <param name="transport">
    /// Canal WinRM à employer. <see cref="WinRmTransportMode.Auto"/> (défaut)
    /// tente HTTPS 5986 et ne se replie en HTTP 5985 que sur échec imputable
    /// au canal chiffré ; <see cref="WinRmTransportMode.Https"/> n'autorise
    /// aucun repli ; <see cref="WinRmTransportMode.Http"/> impose le clair.
    /// </param>
    /// <param name="logger">
    /// Journal de repli. Un passage en clair est un événement de sécurité :
    /// il doit être journalisé, audité et affiché, jamais silencieux.
    /// </param>
    public RemoteExecutor(
        string targetFqdn,
        ConnectionCredentials? credentials = null,
        WinRmTransportMode transport = WinRmTransportMode.Auto,
        ILogger? logger = null)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(targetFqdn);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(targetFqdn));
        }

        _targetFqdn = DomainValidator.NormalizeFqdn(targetFqdn);
        _credentials = credentials;
        _transport = transport;
        _logger = logger;
    }

    public async Task<PowerShellExecutionResult> ExecuteAsync(
        PowerShellCommand command,
        CancellationToken cancellationToken = default)
    {
        var usesHttps = _transport != WinRmTransportMode.Http;

        // Pré-vol : DNS + ports WSMan. Un poste éteint, hors DNS ou sans WinRM
        // doit produire un diagnostic lisible, jamais une enveloppe CLIXML brute
        // avec un code de sortie 1 muet.
        var startedAt = System.Diagnostics.Stopwatch.StartNew();
        var probe = await _connectivityProbe.ProbeAsync(_targetFqdn, cancellationToken).ConfigureAwait(false);
        var selectedPortReachable = usesHttps ? probe.HttpsReachable : probe.HttpReachable;
        if (!probe.Resolved || !selectedPortReachable)
        {
            // En mode Auto, HTTPS est tenté en premier : si seul 5985 répond, on tente
            // quand même le canal HTTP plutôt que de déclarer la cible injoignable.
            if (_transport == WinRmTransportMode.Auto && usesHttps && probe.Resolved && probe.HttpReachable)
            {
                return await RunAsync(command, useHttps: false, cancellationToken).ConfigureAwait(false);
            }

            if (!probe.Resolved)
            {
                _logger?.Warning(
                    "WinRM | cible injoignable " + _targetFqdn + " | commande=" + command.Name
                    + " | " + WinRmConnectivityProbe.Describe(probe));
            }

            return WinRmConnectivityProbe.BuildUnreachableResult(_targetFqdn, probe, _transport, startedAt.Elapsed);
        }

        var result = await RunAsync(command, usesHttps, cancellationToken).ConfigureAwait(false);

        // Le repli n'a lieu qu'en mode Auto, sur HTTPS, et uniquement si l'échec
        // vient du canal chiffré. Un délai dépassé n'est jamais un motif de repli :
        // le contrôleur peut simplement être lent, et rejouer la commande
        // doublerait le délai d'attente de l'appelant.
        if (_transport != WinRmTransportMode.Auto
            || !usesHttps
            || result.ExitCode == 0
            || result.TimedOut)
        {
            return result;
        }

        var reason = FindHttpsFallbackReason(result);
        if (reason is null)
        {
            return result;
        }

        _logger?.Warning(
            "WinRM | repli HTTP " + WinRmHttpPort + " depuis HTTPS " + WinRmHttpsPort
            + " | cible=" + _targetFqdn + " | commande=" + command.Name
            + " | motif=" + reason
            + " | Le canal WSMan n'est plus chiffré.");

        var fallback = await RunAsync(command, useHttps: false, cancellationToken).ConfigureAwait(false);

        return fallback with
        {
            TransportUsed = WinRmTransportMode.Http,
            TransportFallbackReason = reason,
        };
    }

    /// <summary>Exécute la commande sur le transport demandé et y rattache le canal employé.</summary>
    private async Task<PowerShellExecutionResult> RunAsync(
        PowerShellCommand command,
        bool useHttps,
        CancellationToken cancellationToken)
    {
        SecureString? password = null;
        var useExplicitCredentials = false;
        if (_credentials?.Mode == CredentialMode.Explicit)
        {
            var pwd = _credentials.GetPassword();
            if (pwd is not null)
            {
                password = pwd;
                useExplicitCredentials = true;
            }
        }

        // Le bloc interne produit le JSON ; Invoke-Command transporte la chaîne telle quelle.
        var script =
            "$ErrorActionPreference = 'Stop'\n" +
            "$ProgressPreference = 'SilentlyContinue'\n" +
            "$OutputEncoding = [System.Text.Encoding]::UTF8\n" +
            "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n";

        string? stdinPayload = null;
        if (useExplicitCredentials)
        {
            // Transit sécurisé : blob DPAPI par stdin (même utilisateur, même machine).
            var handoff = RemoteCredentialHandoff.FromPassword(_credentials!.UserName!, password!);
            stdinPayload = handoff.ProtectedPasswordBlob;
            password.Dispose();
            password = null;

            script +=
                "$credentialBlob = [Console]::In.ReadToEnd()\n" +
                "$credential = $null\n" +
                "if ($credentialBlob) {\n" +
                "    Add-Type -AssemblyName System.Security -ErrorAction SilentlyContinue\n" +
                "    $protectedBytes = [Convert]::FromBase64String($credentialBlob)\n" +
                "    $plainBytes = [System.Security.Cryptography.ProtectedData]::Unprotect($protectedBytes, $null, [System.Security.Cryptography.DataProtectionScope]::CurrentUser)\n" +
                "    $plain = [System.Text.Encoding]::Unicode.GetString($plainBytes)\n" +
                "    $secure = ConvertTo-SecureString -String $plain -AsPlainText -Force\n" +
                "    $plain = $null\n" +
                "    $credential = New-Object System.Management.Automation.PSCredential('" + InputSanitizer.ToPowerShellSingleQuotedLiteral(_credentials.UserName, nameof(_credentials.UserName)) + "', $secure)\n" +
                "}\n";
        }

        var invoke = "Invoke-Command -ComputerName '" + InputSanitizer.ToPowerShellSingleQuotedLiteral(_targetFqdn, nameof(_targetFqdn)) + "' -ErrorAction Stop";
        if (useHttps)
        {
            // Validation explicite du certificat : AUCUN contournement (pas de SkipCACheck,
            // pas de SkipCNCheck, pas de SkipRevocationCheck, pas de TrustedHosts implicite).
            invoke += " -UseSSL -Port " + WinRmHttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " -SessionOption (New-PSSessionOption -SkipCACheck:$false -SkipCNCheck:$false -SkipRevocationCheck:$false)";
        }
        else
        {
            // Repli HTTP volontairement explicite : configuration ou négociation
            // journalisée uniquement, jamais automatique en mode Https.
            invoke += " -Port " + WinRmHttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (useExplicitCredentials)
        {
            invoke += " -Credential $credential";
        }

        script += invoke + " -ScriptBlock {\n" + command.BuildScript() + "\n}";

        var result = await PowerShellProcessRunner
            .RunAsync(script, command.Timeout, cancellationToken, stdinPayload)
            .ConfigureAwait(false);

        return result with
        {
            TransportUsed = useHttps ? WinRmTransportMode.Https : WinRmTransportMode.Http,
        };
    }

    /// <summary>
    /// Motif du repli HTTPS → HTTP, ou null si l'échec n'est pas imputable
    /// au canal chiffré.
    /// </summary>
    internal static string? FindHttpsFallbackReason(PowerShellExecutionResult result)
    {
        var combined = (result.StandardError + "\n" + result.RawStandardError + "\n" + result.StandardOutput).ToLowerInvariant();
        foreach (var marker in HttpsFallbackMarkers)
        {
            if (combined.Contains(marker, StringComparison.Ordinal))
            {
                return marker;
            }
        }

        return null;
    }
}