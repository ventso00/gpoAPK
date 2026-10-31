using System.Text.Json;
using ADOps.GpoManager.Core.Models;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Tests de sérialisation des modèles — Étape 3 du cahier des charges.</summary>
public class GpoSummarySerializationTests
{
    [Fact]
    public void GpoSummary_RoundTripsThroughSystemTextJson()
    {
        var gpo = new GpoSummary
        {
            Id = Guid.NewGuid(),
            DisplayName = "GPO-Security",
            Description = "Baseline de sécurité — accents é è à ç",
            CreationTime = new DateTimeOffset(2024, 1, 15, 9, 30, 0, TimeSpan.FromHours(1)),
            ModificationTime = new DateTimeOffset(2026, 9, 20, 14, 5, 12, TimeSpan.FromHours(2)),
            Status = GpoStatus.AllSettingsEnabled,
            DomainName = "ventso.test",
        };

        var json = JsonSerializer.Serialize(new[] { gpo });
        var restored = JsonSerializer.Deserialize<GpoSummary[]>(json);

        var restoredGpo = Assert.Single(restored!);
        Assert.Equal(gpo.Id, restoredGpo!.Id);
        Assert.Equal(gpo.DisplayName, restoredGpo.DisplayName);
        Assert.Equal(gpo.Description, restoredGpo.Description);
        Assert.Equal(gpo.Status, restoredGpo.Status);
        Assert.Equal(gpo.CreationTime, restoredGpo.CreationTime);
        Assert.Equal(gpo.ModificationTime, restoredGpo.ModificationTime);
        Assert.Equal(gpo.DomainName, restoredGpo.DomainName);
    }

    [Fact]
    public void StatusText_ReturnsLocalizedLabel()
    {
        Assert.Equal("Tout activé", new GpoSummary { Status = GpoStatus.AllSettingsEnabled }.StatusText);
        Assert.Equal("Config. ordinateur désactivée", new GpoSummary { Status = GpoStatus.ComputerSettingsDisabled }.StatusText);
        Assert.Equal("Config. utilisateur désactivée", new GpoSummary { Status = GpoStatus.UserSettingsDisabled }.StatusText);
        Assert.Equal("Tout désactivé", new GpoSummary { Status = GpoStatus.AllSettingsDisabled }.StatusText);
        Assert.Equal("Inconnu", new GpoSummary { Status = GpoStatus.Unknown }.StatusText);
    }

    [Fact]
    public void OperationError_ToDisplayString_IncludesCauseAndRemediation()
    {
        var error = new OperationError("Titre", "La cause", "La résolution");
        var text = error.ToDisplayString();
        Assert.Contains("Titre", text);
        Assert.Contains("Cause probable : La cause", text);
        Assert.Contains("Résolution : La résolution", text);
    }

    [Fact]
    public void OperationResult_SuccessAndFailure()
    {
        var success = OperationResult<string>.Success("valeur");
        Assert.True(success.IsSuccess);
        Assert.Equal("valeur", success.Value);
        Assert.Null(success.Error);

        var failure = OperationResult<string>.Failure(new OperationError("Erreur"));
        Assert.False(failure.IsSuccess);
        Assert.Null(failure.Value);
        Assert.NotNull(failure.Error);
    }

    [Fact]
    public void AuditEntry_DefaultsAndModel()
    {
        var entry = new AuditEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            UserName = "VENTSO\\admin",
            Action = "HealthCheck",
            Target = "ad-ds01.ventso.test",
            Result = AuditResult.Success,
            Message = "OK",
        };

        Assert.Equal("VENTSO\\admin", entry.UserName);
        Assert.Equal(AuditResult.Success, entry.Result);
        Assert.Null(entry.Details);
    }
}
