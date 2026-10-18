namespace Diligencia.Modules.Identidad.Contracts;

public interface IActorContext
{
    string ActorId { get; }        // claim "sub" o "system:<ApplicationName>"
    ActorSurface Surface { get; }
}

public enum ActorSurface { Backoffice, Portal, System }

public static class AuthSchemes
{
    public const string Backoffice = "Backoffice";
    public const string Portal = "Portal";
}

public static class BackofficeRoles
{
    public const string Solicitante = "Solicitante";
    public const string Analista = "Analista";
    public const string Aprobador = "Aprobador";
    public const string Administrador = "Administrador";
    public const string Auditor = "Auditor";
}

public static class AuthPolicies
{
    public const string Backoffice = "Backoffice";
    public const string Portal = "Portal";
    public const string AuditReader = "AuditReader";
    public const string Administrator = "Administrator";
}
