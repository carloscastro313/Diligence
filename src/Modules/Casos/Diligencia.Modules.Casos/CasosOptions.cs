namespace Diligencia.Modules.Casos;

internal sealed class CasosOptions
{
    public const string Section = "Casos";

    public string PortalBaseUrl { get; set; } = "";
    public int AccesoRateLimitPerMinute { get; set; } = 10;
}
