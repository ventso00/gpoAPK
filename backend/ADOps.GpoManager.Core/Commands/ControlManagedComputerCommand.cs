using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>Action de puissance exécutée sur le poste client.</summary>
public enum ManagedPowerAction
{
    Restart,
    Shutdown,
}

/// <summary>
/// Redémarre ou éteint un poste client depuis le poste lui-même (via WinRM).
/// Un délai de 15 secondes laisse à l'utilisateur le temps d'enregistrer son
/// travail ; les serveurs et le contrôleur de domaine sont refusés en amont.
/// </summary>
public sealed class ControlManagedComputerCommand : PowerShellCommand
{
    public string ComputerName { get; }
    public ManagedPowerAction Action { get; }
    public int DelaySeconds { get; }

    public override string Name => Action == ManagedPowerAction.Restart ? "RestartManagedComputer" : "ShutdownManagedComputer";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public ControlManagedComputerCommand(string computerName, ManagedPowerAction action, int delaySeconds = 15)
    {
        var (isValid, error) = DomainValidator.ValidateServerFqdn(computerName);
        if (!isValid)
        {
            throw new ArgumentException(error, nameof(computerName));
        }

        ComputerName = computerName.Trim();
        Action = action;
        DelaySeconds = Math.Clamp(delaySeconds, 5, 120);
    }

    public override string ScriptBody
    {
        get
        {
            var argument = Action == ManagedPowerAction.Restart ? "/r" : "/s";
            return
                "$ErrorActionPreference = 'Stop'\n" +
                "$os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop\n" +
                "if ($os.Caption -match 'Server') { throw 'Refusé : la cible est un système serveur, pas un poste client.' }\n" +
                "$delay = " + DelaySeconds + "\n" +
                "$result = & shutdown.exe " + argument + " /t $delay /f /d p:4:1 2>&1\n" +
                "if ($LASTEXITCODE -ne 0) { throw ('shutdown.exe a échoué (code ' + $LASTEXITCODE + ') : ' + ($result | Out-String).Trim()) }\n" +
                "[PSCustomObject]@{\n" +
                "    Computer     = $env:COMPUTERNAME\n" +
                "    Action       = '" + Action.ToString() + "'\n" +
                "    DelaySeconds = $delay\n" +
                "    Caption      = $os.Caption\n" +
                "    Timestamp    = (Get-Date).ToUniversalTime().ToString('o')\n" +
                "}";
        }
    }
}
