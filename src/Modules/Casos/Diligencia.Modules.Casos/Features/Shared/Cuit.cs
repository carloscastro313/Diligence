using Diligencia.Modules.Terceros.Contracts;

namespace Diligencia.Modules.Casos.Features.Shared;

internal static class Cuit
{
    private static readonly int[] Pesos = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    public static string Normalize(string? cuit) => new((cuit ?? "").Where(char.IsAsciiDigit).ToArray());

    public static bool IsValid(string? cuit)
    {
        var d = Normalize(cuit);
        if (d.Length != 11) return false;
        var suma = 0;
        for (var i = 0; i < 10; i++) suma += (d[i] - '0') * Pesos[i];
        var r = 11 - suma % 11;
        if (r == 10) return false;
        return (r == 11 ? 0 : r) == d[10] - '0';
    }

    public static bool MatchesPersona(string? cuit, TipoPersona persona)
    {
        var d = Normalize(cuit);
        if (d.Length < 2) return false;
        return d[..2] switch
        {
            "20" or "23" or "24" or "27" => persona == TipoPersona.Humana,
            "30" or "33" or "34" => persona == TipoPersona.Juridica,
            _ => false,
        };
    }
}
