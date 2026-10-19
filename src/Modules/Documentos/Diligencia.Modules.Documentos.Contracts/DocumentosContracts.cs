using System.Data.Common;

namespace Diligencia.Modules.Documentos.Contracts;

public sealed record NuevoDocumento(Guid CasoId, Guid TerceroId, string Tipo, string NombreOriginal);

public sealed record DocumentoInfo(
    Guid Id, Guid CasoId, string Tipo, string NombreOriginal, string ContentType, long TamanoBytes, DateTime CargadoUtc);

public enum MotivoRechazo { FormatoNoPermitido, Ilegible, DemasiadoGrande }

public sealed record DocumentoRechazado(MotivoRechazo Motivo, long TamanoMaximoBytes);

public interface IDocumentStore
{
    // Tamaño máximo vigente (lectura sin transacción; lo usa el portal para mostrarlo y cortar temprano).
    Task<long> GetTamanoMaximoBytesAsync(CancellationToken cancellationToken = default);

    // Dentro de la transacción del llamador: verifica tamaño y contenido real, cifra, guarda, da de baja
    // como Reemplazado el vigente del mismo (CasoId, Tipo) y registra la bitácora. No confirma ni revierte.
    // Devuelve el rechazo en vez de lanzar: es un resultado esperado, no un error.
    Task<(DocumentoInfo? Documento, DocumentoRechazado? Rechazo)> PutAsync(
        NuevoDocumento documento, Stream contenido, DbTransaction transaction, CancellationToken cancellationToken = default);

    // Baja lógica como Eliminado del vigente de (casoId, tipo) + bitácora. false si no había vigente.
    Task<bool> RemoveAsync(Guid casoId, string tipo, DbTransaction transaction, CancellationToken cancellationToken = default);

    // Vigentes de un caso (sin contenido). Con transaction, lee dentro de ella (para el envío).
    Task<IReadOnlyList<DocumentoInfo>> ListVigentesAsync(
        Guid casoId, DbTransaction? transaction = null, CancellationToken cancellationToken = default);
}
