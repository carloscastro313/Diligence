using System.Data.Common;

namespace Diligencia.Modules.Identidad.Contracts;

public interface IActorContext
{
    string ActorId { get; }        // claim "sub" o "system:<ApplicationName>"
    ActorSurface Surface { get; }
}

public interface IPortalSignIn
{
    // Crea la sesión de portal dentro de la conexión y transacción del llamador, escribe la cookie dlg_pt en la
    // respuesta actual y hace que el resto de la request actúe como "tercero:{terceroId}" (IActorContext.ActorId),
    // para que la bitácora del ingreso tenga autor. No confirma ni revierte.
    Task SignInAsync(Guid casoId, Guid terceroId, DbTransaction transaction, CancellationToken cancellationToken = default);
}

public interface IPortalActor
{
    // Caso y tercero de la sesión de portal de la request actual (claims cargados desde la base al validar la sesión).
    // Lanza InvalidOperationException fuera de una request de portal autenticada.
    Guid CasoId { get; }
    Guid TerceroId { get; }
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
    public const string UserDirectoryReader = "UserDirectoryReader";
}
