using ADOps.GpoManager.Core.Validation;

namespace ADOps.GpoManager.Core.Commands;

/// <summary>
/// Génère le rapport XML ou HTML d'une GPO via Get-GPOReport.
/// L'identifiant de GPO est validé (Guid) avant injection dans le script.
/// </summary>
public sealed class GetGpoReportCommand : PowerShellCommand
{
    public Guid GpoId { get; }
    public string ReportType { get; }

    public override string Name => "GetGpoReport";
    public override TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public GetGpoReportCommand(Guid gpoId, string reportType)
    {
        if (gpoId == Guid.Empty)
        {
            throw new ArgumentException("L'identifiant de GPO est requis.", nameof(gpoId));
        }

        if (!string.Equals(reportType, "Xml", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(reportType, "Html", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Le type de rapport doit être « Xml » ou « Html ».", nameof(reportType));
        }

        GpoId = gpoId;
        ReportType = reportType;
    }

    public override string ScriptBody =>
            "Import-Module GroupPolicy -ErrorAction Stop\n"
            + "Get-GPOReport -Guid '" + GpoId + "' -ReportType " + ReportType + ";";
}
