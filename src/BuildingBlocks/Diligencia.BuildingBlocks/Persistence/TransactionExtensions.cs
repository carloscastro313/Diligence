using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Diligencia.BuildingBlocks.Persistence;

public static class TransactionExtensions
{
    // Con EnableRetryOnFailure toda transacción manual debe ir dentro de la estrategia de ejecución.
    // El trabajo debe llamar SaveChangesAsync(acceptAllChangesOnSuccess: false).
    public static Task ExecuteInTransactionAsync(
        this DbContext db,
        Func<DbTransaction, CancellationToken, Task> work,
        CancellationToken cancellationToken = default) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await work(tx.GetDbTransaction(), ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.AcceptAllChanges();
        }, cancellationToken);
}
