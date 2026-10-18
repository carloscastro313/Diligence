# Contratos públicos tocados

Todos los cambios son **aditivos**.

## Identidad.Contracts

Sin cambios. Casos usa `BackofficeRoles.Solicitante` (ya público) en el subgrupo `/solicitudes`
(research R9). El interior de Identidad no se modifica (Principio I).

## Terceros.Contracts (nuevo contenido; el proyecto ya existe vacío)

```csharp
namespace Diligencia.Modules.Terceros.Contracts;

public enum TipoTercero { Cliente, Proveedor }
public enum TipoPersona { Humana, Juridica }
public enum EstadoTercero { EnAlta, Activo, Bloqueado, Inactivo }

public sealed record TerceroInfo(
    Guid Id, string Cuit, TipoTercero Tipo, TipoPersona Persona, string RazonSocial, EstadoTercero Estado);

public sealed record NuevoTercero(string Cuit, TipoTercero Tipo, TipoPersona Persona, string RazonSocial);

public interface ITerceroRegistry
{
    // Lectura sin bloqueo para el aviso de duplicado (RF-02). Cuit ya normalizado.
    Task<TerceroInfo?> FindByCuitAsync(string cuit, CancellationToken cancellationToken = default);

    // Dentro de la conexión y transacción del llamador; bloquea el CUIT (UPDLOCK, HOLDLOCK) hasta que
    // el llamador confirme. Si no existe lo crea en EnAlta y registra la bitácora en la misma tx.
    // No confirma ni revierte. Created = true si lo creó esta llamada.
    Task<(TerceroInfo Tercero, bool Created)> GetOrCreateForOnboardingAsync(
        NuevoTercero tercero, DbTransaction transaction, CancellationToken cancellationToken = default);
}
```

- `TercerosModule.AddTercerosModule` registra `ITerceroRegistry`.
- Sin endpoints propios en esta feature.

## BuildingBlocks (`Diligencia.BuildingBlocks.Email`, nuevo)

```csharp
public interface IEmailTransport
{
    // Lanza si el servidor no acepta el mensaje. Nunca registra el cuerpo.
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public static class EmailSetup
{
    public static IServiceCollection AddEmailTransport(this IServiceCollection services); // SmtpEmailTransport, lee Email:Smtp
}
```

API y worker llaman `AddEmailTransport()`. `Identidad.SmtpEmailSender` no cambia (research R7).

## Casos (superficie pública del módulo)

```csharp
public static class CasosModule
{
    IServiceCollection AddCasosModule(IServiceCollection, IConfiguration); // + handlers, validadores, IPortalLinkDelivery, opciones Casos
    ApiSurfaces MapCasosEndpoints(ApiSurfaces);                            // + /solicitudes en Backoffice
}
```

`Casos.Contracts` no cambia: ningún otro módulo consume casos todavía. `IPortalLinkDelivery` (job de
Hangfire), entidades, handlers y `CasosDbContext` son `internal`.

## Configuración nueva

| Clave | Quién la lee | Valor local (AppHost) |
|---|---|---|
| `Casos:PortalBaseUrl` | worker (arma el enlace) | endpoint http de `portal` |
| `Email:Smtp:Host`, `Email:Smtp:Port` | worker (ya la tenía la API) | Mailpit |
