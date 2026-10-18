using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Diligencia.BuildingBlocks.Correlation;

public static partial class CorrelationContext
{
    private static readonly AsyncLocal<string?> Value = new();

    public static string Current =>
        Value.Value
        ?? Activity.Current?.TraceId.ToString()
        ?? Guid.NewGuid().ToString("N");

    public static bool IsValid(string? id) => id is not null && ValidId().IsMatch(id);

    public static void Set(string id)
    {
        if (!IsValid(id))
            throw new ArgumentException("Identificador de correlación inválido.", nameof(id));
        Value.Value = id;
    }

    public static void Clear() => Value.Value = null;

    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex ValidId();
}
