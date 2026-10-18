namespace Diligencia.IntegrationTests.Support;

public static class TestCuits
{
    private static readonly int[] Weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];

    /// <summary>CUIT válido y único por llamada: prefijo de 2 dígitos + 8 aleatorios + DV (módulo 11).</summary>
    public static string New(string prefix = "30")
    {
        while (true)
        {
            var body = prefix + Random.Shared.Next(0, 100_000_000).ToString("D8");
            var sum = 0;
            for (var i = 0; i < 10; i++) sum += (body[i] - '0') * Weights[i];
            var dv = 11 - sum % 11;
            if (dv == 11) dv = 0;
            if (dv != 10) return body + dv;
        }
    }
}
