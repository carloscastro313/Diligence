using System.Text.Json;
using Diligencia.Modules.Auditoria.Contracts;

namespace Diligencia.Modules.Auditoria.Features.RecordEvent;

internal static class AuditValueMasker
{
    public const string Mask = "***";
    public const int MaxValueLength = 4000;
    public const string TruncatedSuffix = "…[truncado]";

    private static readonly string[] SensitiveNameParts = ["password", "secret", "token"];

    /// <summary>Descarta los cambios iguales, enmascara los sensibles y serializa a JSON {campo: valor}.</summary>
    public static (string? OldValues, string? NewValues) Build(IReadOnlyList<AuditChange>? changes)
    {
        if (changes is null) return (null, null);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var old = new Dictionary<string, string?>();
        var @new = new Dictionary<string, string?>();

        foreach (var change in changes)
        {
            if (string.IsNullOrWhiteSpace(change.Field))
                throw new ArgumentException("Un cambio de auditoría requiere el nombre del campo.", nameof(changes));
            if (!seen.Add(change.Field))
                throw new ArgumentException($"El campo '{change.Field}' está repetido en los cambios.", nameof(changes));
            if (change.OldValue == change.NewValue) continue;

            var masked = change.IsSensitive || IsSensitiveName(change.Field);
            old[change.Field] = masked ? Mask : Truncate(change.OldValue);
            @new[change.Field] = masked ? Mask : Truncate(change.NewValue);
        }

        return old.Count == 0 ? (null, null) : (JsonSerializer.Serialize(old), JsonSerializer.Serialize(@new));
    }

    private static bool IsSensitiveName(string field) =>
        SensitiveNameParts.Any(p => field.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static string? Truncate(string? value) =>
        value is { Length: > MaxValueLength } ? value[..MaxValueLength] + TruncatedSuffix : value;
}
