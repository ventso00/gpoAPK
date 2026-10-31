namespace ADOps.GpoManager.Core.Commands;

/// <summary>Commande PowerShell typée construite à partir d'un corps de script et d'un délai.</summary>
public sealed class SimplePowerShellCommand : PowerShellCommand
{
    public override string Name { get; }
    public override string ScriptBody { get; }
    public override TimeSpan Timeout { get; }

    public SimplePowerShellCommand(string name, string scriptBody, TimeSpan? timeout = null)
    {
        Name = name;
        ScriptBody = scriptBody;
        Timeout = timeout ?? TimeSpan.FromMinutes(2);
    }
}
