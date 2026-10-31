namespace ADOps.GpoManager.Core.Services;

public enum LogSeverity
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
}

/// <summary>Journalisation minimale (fichier texte avec rotation, implémentée dans Infrastructure).</summary>
public interface ILogger
{
    void Trace(string message, Exception? exception = null);
    void Debug(string message, Exception? exception = null);
    void Information(string message, Exception? exception = null);
    void Warning(string message, Exception? exception = null);
    void Error(string message, Exception? exception = null);
    void Critical(string message, Exception? exception = null);
}
