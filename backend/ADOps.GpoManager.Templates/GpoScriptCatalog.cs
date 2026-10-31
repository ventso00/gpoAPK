using System.Reflection;
using System.Text.Json;
using ADOps.GpoManager.Core.Services;

namespace ADOps.GpoManager.Templates;

/// <summary>
/// Charge le catalogue consolidé <c>gpo_catalog.json</c> (embarqué) et expose
/// les scripts PowerShell réels de chaque GPO.
/// </summary>
public sealed class GpoScriptCatalog : IGpoScriptCatalog
{
    private readonly Dictionary<string, string> _scripts;
    private readonly List<string> _ids;

    public GpoScriptCatalog()
    {
        _scripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _ids = new List<string>();

        var assembly = typeof(GpoScriptCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream("gpo_catalog.json");
        if (stream is null)
        {
            return;
        }

        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("gpos", out var gpos) || gpos.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var gpo in gpos.EnumerateArray())
        {
            var id = gpo.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) { continue; }

            string? fullScript = null;
            if (gpo.TryGetProperty("powershell", out var ps) && ps.ValueKind == JsonValueKind.Object
                && ps.TryGetProperty("fullScript", out var fs))
            {
                fullScript = fs.GetString();
            }

            if (!string.IsNullOrWhiteSpace(fullScript))
            {
                _scripts[id!] = fullScript!;
                _ids.Add(id!);
            }
        }
    }

    public string? GetFullScript(string gpoId)
        => !string.IsNullOrWhiteSpace(gpoId) && _scripts.TryGetValue(gpoId.Trim(), out var s) ? s : null;

    public IReadOnlyList<string> GetIds() => _ids;
}
