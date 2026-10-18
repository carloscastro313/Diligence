using System.Data.Common;

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
