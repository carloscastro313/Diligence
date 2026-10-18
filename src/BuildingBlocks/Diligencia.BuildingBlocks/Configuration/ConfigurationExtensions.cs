using Microsoft.Extensions.Configuration;

namespace Diligencia.BuildingBlocks.Configuration;

public static class ConfigurationExtensions
{
    // Nunca incluye el valor: puede ser un secreto.
    public static string GetRequired(this IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Falta la configuración obligatoria '{key}'.")
            : value;
    }
}
