using System.Data.Common;
using System.Security.Cryptography;
using Diligencia.BuildingBlocks.Persistence;
using Diligencia.Modules.Auditoria.Contracts;
using Diligencia.Modules.Documentos.Contracts;
using Diligencia.Modules.Documentos.Persistence;
using Diligencia.Modules.Identidad.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Diligencia.Modules.Documentos.Features.Store;

// Recibir, hashear, cifrar y guardar es almacenamiento de E/S acotado por el tamaño máximo, no procesamiento del
// contenido (Principio VI). Nunca registra bytes: la bitácora lleva solo tipo, nombre, tamaño y hash.
internal sealed class DocumentStore(
    DocumentosDbContext db, IAuditLog audit, DocumentCipher cipher, IActorContext actor,
    TimestampsInterceptor timestamps, TimeProvider clock) : IDocumentStore
{
    public Task<long> GetTamanoMaximoBytesAsync(CancellationToken cancellationToken = default) =>
        db.Parametros.AsNoTracking().Select(p => p.TamanoMaximoBytes).SingleAsync(cancellationToken);

    public async Task<(DocumentoInfo? Documento, DocumentoRechazado? Rechazo)> PutAsync(
        NuevoDocumento documento, Stream contenido, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var ctx = await OpenAsync(transaction, cancellationToken);
        var maximo = await ctx.Parametros.AsNoTracking().Select(p => p.TamanoMaximoBytes).SingleAsync(cancellationToken);

        var bytes = await ReadUpToAsync(contenido, maximo, cancellationToken);
        if (bytes is null) return (null, new DocumentoRechazado(MotivoRechazo.DemasiadoGrande, maximo));

        using var buffer = new MemoryStream(bytes, writable: false);
        var kind = ContentInspector.Detect(buffer);
        if (ContentInspector.ContentTypeOf(kind) is not { } contentType)
            return (null, new DocumentoRechazado(kind == ContentKind.Ilegible ? MotivoRechazo.Ilegible : MotivoRechazo.FormatoNoPermitido, maximo));

        var now = clock.GetUtcNow().UtcDateTime;
        var nombre = Path.GetFileName(documento.NombreOriginal.Replace('\\', '/'));
        if (nombre.Length > 255) nombre = nombre[..255];

        var previo = await ctx.Documentos.SingleOrDefaultAsync(
            d => d.CasoId == documento.CasoId && d.Tipo == documento.Tipo && d.BajaUtc == null, cancellationToken);
        if (previo is not null)
        {
            previo.BajaUtc = now;
            previo.Baja = MotivoBaja.Reemplazado;
            // Dos guardados: el índice único filtrado no admite dos vigentes ni siquiera dentro de la misma tanda.
            await ctx.SaveChangesAsync(cancellationToken);
        }

        var sha = SHA256.HashData(bytes);
        var nuevo = new Documento
        {
            CasoId = documento.CasoId, TerceroId = documento.TerceroId, Tipo = documento.Tipo, NombreOriginal = nombre,
            ContentType = contentType, TamanoBytes = bytes.Length, Sha256 = sha, CargadoPor = actor.ActorId, CargadoUtc = now,
        };
        var (nonce, tag, cifrado) = cipher.Encrypt(bytes);
        ctx.Documentos.Add(nuevo);
        ctx.Contenidos.Add(new DocumentoContenido { DocumentoId = nuevo.Id, Nonce = nonce, Tag = tag, Cifrado = cifrado });
        await ctx.SaveChangesAsync(cancellationToken);

        if (previo is not null)
            await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "Documento", previo.Id.ToString(),
            [
                new AuditChange("Tipo", null, previo.Tipo),
                new AuditChange("NombreOriginal", null, previo.NombreOriginal),
                new AuditChange("TamanoBytes", null, previo.TamanoBytes.ToString()),
                new AuditChange("Sha256", null, Convert.ToHexString(previo.Sha256)),
                new AuditChange("Baja", null, nameof(MotivoBaja.Reemplazado)),
            ]), transaction, cancellationToken);
        await audit.RecordAsync(new AuditEntry(AuditAction.Created, "Documento", nuevo.Id.ToString(),
        [
            new AuditChange("Caso", null, nuevo.CasoId.ToString()),
            new AuditChange("Tipo", null, nuevo.Tipo),
            new AuditChange("NombreOriginal", null, nuevo.NombreOriginal),
            new AuditChange("ContentType", null, nuevo.ContentType),
            new AuditChange("TamanoBytes", null, nuevo.TamanoBytes.ToString()),
            new AuditChange("Sha256", null, Convert.ToHexString(sha)),
        ]), transaction, cancellationToken);

        return (ToInfo(nuevo), null);
    }

    public async Task<bool> RemoveAsync(Guid casoId, string tipo, DbTransaction transaction, CancellationToken cancellationToken = default)
    {
        await using var ctx = await OpenAsync(transaction, cancellationToken);
        var vigente = await ctx.Documentos.SingleOrDefaultAsync(d => d.CasoId == casoId && d.Tipo == tipo && d.BajaUtc == null, cancellationToken);
        if (vigente is null) return false;

        vigente.BajaUtc = clock.GetUtcNow().UtcDateTime;
        vigente.Baja = MotivoBaja.Eliminado;
        await ctx.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(new AuditEntry(AuditAction.Updated, "Documento", vigente.Id.ToString(),
        [
            new AuditChange("Tipo", null, vigente.Tipo),
            new AuditChange("NombreOriginal", null, vigente.NombreOriginal),
            new AuditChange("TamanoBytes", null, vigente.TamanoBytes.ToString()),
            new AuditChange("Sha256", null, Convert.ToHexString(vigente.Sha256)),
            new AuditChange("Baja", null, nameof(MotivoBaja.Eliminado)),
        ]), transaction, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<DocumentoInfo>> ListVigentesAsync(
        Guid casoId, DbTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        await using var scoped = transaction is null ? null : await OpenAsync(transaction, cancellationToken);
        var ctx = scoped ?? db;
        var rows = await ctx.Documentos.AsNoTracking().Where(d => d.CasoId == casoId && d.BajaUtc == null)
            .OrderBy(d => d.CargadoUtc).ToListAsync(cancellationToken);
        return [.. rows.Select(ToInfo)];
    }

    // Contexto efímero sobre la conexión y transacción del llamador (patrón de TerceroRegistry); no confirma ni revierte.
    private async Task<DocumentosDbContext> OpenAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var connection = transaction.Connection ?? throw new ArgumentException("La transacción debe tener una conexión abierta.", nameof(transaction));
        var options = new DbContextOptionsBuilder<DocumentosDbContext>().UseSqlServer(connection).AddInterceptors(timestamps).Options;
        var ctx = new DocumentosDbContext(options);
        await ctx.Database.UseTransactionAsync(transaction, cancellationToken);
        return ctx;
    }

    // null = supera el máximo; corta sin leer el resto.
    private static async Task<byte[]?> ReadUpToAsync(Stream stream, long maximo, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maximo) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static DocumentoInfo ToInfo(Documento d) => new(
        d.Id, d.CasoId, d.Tipo, d.NombreOriginal, d.ContentType, d.TamanoBytes, DateTime.SpecifyKind(d.CargadoUtc, DateTimeKind.Utc));
}
