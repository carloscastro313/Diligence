using System.Text.Json;
using Diligencia.Modules.Auditoria.Contracts;

namespace Diligencia.Modules.Casos.Features.Shared;

internal static class JsonDiff
{
    // Aplana ambos JSON a `ruta -> valor` (`beneficiarios[0].porcentaje`) y devuelve solo las rutas distintas.
    public static IReadOnlyList<AuditChange> Diff(string? oldJson, string? newJson)
    {
        var before = Flatten(oldJson);
        var after = Flatten(newJson);
        return before.Keys.Union(after.Keys)
            .Select(path => new AuditChange(path, before.GetValueOrDefault(path), after.GetValueOrDefault(path)))
            .Where(c => c.OldValue != c.NewValue)
            .ToList();
    }

    private static Dictionary<string, string?> Flatten(string? json)
    {
        var result = new Dictionary<string, string?>();
        if (!string.IsNullOrWhiteSpace(json))
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, "", result);
        }
        return result;
    }

    private static void Walk(JsonElement e, string path, Dictionary<string, string?> into)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Walk(p.Value, path.Length == 0 ? p.Name : $"{path}.{p.Name}", into);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in e.EnumerateArray()) Walk(item, $"{path}[{i++}]", into);
                break;
            case JsonValueKind.Null: break; // null equivale a ausente
            case JsonValueKind.String: into[path] = e.GetString(); break;
            default: into[path] = e.GetRawText(); break;
        }
    }
}
