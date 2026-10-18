# Contratos públicos C# tocados

Cambios en los proyectos `Contracts` (Principio III). Todos son **aditivos**: ningún consumidor
existente se rompe.

## Identidad.Contracts (`IdentityContracts.cs`)

```csharp
public static class AuthPolicies
{
    public const string Backoffice = "Backoffice";
    public const string Portal = "Portal";
    public const string AuditReader = "AuditReader";            // Administrador | Auditor
    public const string Administrator = "Administrator";        // Administrador
    public const string UserDirectoryReader = "UserDirectoryReader"; // nuevo: Administrador | Auditor
}
```

- `BackofficeRoles` no cambia: sus cinco constantes son los valores válidos de `UserRole.Role` y del
  claim `role`.
- `IActorContext.ActorId` sigue siendo el claim `sub`; desde esta feature es el `Id` del usuario
  (GUID) en vez de un nombre libre de `user-jwts`.
- No se publica ninguna interfaz de consulta de usuarios para otros módulos: ninguno la necesita
  todavía. Cuando Casos/Aprobaciones necesiten nombre o área de un usuario, se agrega aquí
  (p. ej. `IUserDirectory`).

## Auditoria.Contracts (`AuditContracts.cs`)

```csharp
public enum AuditAction
{
    Created, Updated, SensitiveDataViewed, Downloaded, Decision,   // existentes, mismo orden
    LoginSucceeded, LoginFailed, LoggedOut, AccountLocked,         // nuevos
    PasswordResetRequested, PasswordSet, AccessDenied,
}
```

- Se persiste como texto (`AuditEvent.Action`, `nvarchar(32)`): agregar valores no requiere
  migración en `auditoria`.

## Superficie del módulo Identidad (pública por la excepción ya justificada en la 001)

```csharp
public static class IdentidadModule
{
    IServiceCollection AddIdentidadModule(IServiceCollection, IConfiguration);        // + options, email, TimeProvider
    IServiceCollection AddIdentidadAuthentication(IServiceCollection, IConfiguration); // + validación de sesión, rate limiter, handler AccessDenied
    ApiSurfaces MapIdentidadEndpoints(ApiSurfaces);                                    // mapea /auth, /users, /areas en Backoffice
    Task BootstrapAdministratorAsync(IServiceProvider, CancellationToken);             // nuevo: lo llama el MigrationService
}
```

Todo lo demás (entidades, handlers, validadores, `IEmailSender`, `IdentidadDbContext`) es
`internal`.
