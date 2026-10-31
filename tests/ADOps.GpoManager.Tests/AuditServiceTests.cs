using ADOps.GpoManager.Core.Models;
using ADOps.GpoManager.Core.Services;
using ADOps.GpoManager.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ADOps.GpoManager.Tests;

/// <summary>Tests du service d'audit SQLite : chaque opération doit être enregistrée (contrainte n°5).</summary>
public class AuditServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "adops-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteDatabase _database;
    private readonly AuditService _service;

    public AuditServiceTests()
    {
        _database = new SqliteDatabase(Path.Combine(_tempDir, "audit.db"));
        var repository = new AuditRepository(_database);
        _service = new AuditService(repository, new SilentLogger());
    }

    [Fact]
    public async Task LogAsync_ThenGetRecentAsync_ReturnsEntry()
    {
        await _service.LogAsync("HealthCheck", "ad-ds01.ventso.test", AuditResult.Success, "Connexion réussie.");

        var entries = await _service.GetRecentAsync(10);
        var entry = Assert.Single(entries);

        Assert.Equal("HealthCheck", entry.Action);
        Assert.Equal("ad-ds01.ventso.test", entry.Target);
        Assert.Equal(AuditResult.Success, entry.Result);
        Assert.Equal("Connexion réussie.", entry.Message);
        Assert.NotEmpty(entry.UserName);
        Assert.True(entry.TimestampUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task LogAsync_StoresDetails()
    {
        await _service.LogAsync("GpoList", "ad-ds01.ventso.test", AuditResult.Success, "3 GPO(s) chargée(s).", "Durée : 1,2 s");

        var entry = Assert.Single(await _service.GetRecentAsync(10));
        Assert.Equal("Durée : 1,2 s", entry.Details);
    }

    [Fact]
    public async Task GetCountAsync_ReturnsNumberOfEntries()
    {
        await _service.LogAsync("HealthCheck", "ad-ds01.ventso.test", AuditResult.Success, "OK");
        await _service.LogAsync("GpoList", "ad-ds01.ventso.test", AuditResult.Success, "OK");

        Assert.Equal(2, await _service.GetCountAsync());
    }

    [Fact]
    public async Task GetRecentAsync_LimitsResults()
    {
        for (var i = 0; i < 5; i++)
        {
            await _service.LogAsync("HealthCheck", "ad-ds01.ventso.test", AuditResult.Information, "Entrée " + i);
        }

        var entries = await _service.GetRecentAsync(3);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public async Task LogAsync_WithNullDetails_StoresNull()
    {
        await _service.LogAsync("HealthCheck", "ad-ds01.ventso.test", AuditResult.Failure, "Échec");

        var entry = Assert.Single(await _service.GetRecentAsync(10));
        Assert.Null(entry.Details);
    }

    public void Dispose()
    {
        _database.Dispose();
        // Les connexions SQLite sont poolées : libérer les handles avant la suppression.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private sealed class SilentLogger : ILogger
    {
        public void Trace(string message, Exception? exception = null) { }
        public void Debug(string message, Exception? exception = null) { }
        public void Information(string message, Exception? exception = null) { }
        public void Warning(string message, Exception? exception = null) { }
        public void Error(string message, Exception? exception = null) { }
        public void Critical(string message, Exception? exception = null) { }
    }
}
