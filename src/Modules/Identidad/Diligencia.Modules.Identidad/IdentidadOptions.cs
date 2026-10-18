namespace Diligencia.Modules.Identidad;

internal sealed class IdentidadOptions
{
    public const string Section = "Identidad";

    public int SessionIdleMinutes { get; set; } = 30;
    public int SessionAbsoluteHours { get; set; } = 8;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int ResetTokenMinutes { get; set; } = 60;
    public int InvitationTokenHours { get; set; } = 72;
    public int AuthRateLimitPerMinute { get; set; } = 10;
    public string BackofficeBaseUrl { get; set; } = "";
    public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();
}

internal sealed class BootstrapAdminOptions
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? Area { get; set; }
}
