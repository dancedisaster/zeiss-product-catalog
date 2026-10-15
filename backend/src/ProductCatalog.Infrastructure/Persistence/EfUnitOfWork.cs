using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Application.Common;

namespace ProductCatalog.Infrastructure.Persistence;

/// <summary>Translates provider-specific failures into the application's own exceptions.</summary>
internal sealed class EfUnitOfWork(CatalogDbContext db) : IUnitOfWork
{
    // SQL Server: 2601 = duplicate key in unique index, 2627 = unique/PK constraint violation.
    private static readonly int[] UniqueViolationErrorNumbers = [2601, 2627];

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Forget the stale entities so a retry reads fresh values from the database.
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException("The row was modified or deleted by another request.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationErrorNumbers.Contains(sql.Number))
        {
            db.ChangeTracker.Clear();
            throw new UniqueConstraintViolationException("A unique constraint was violated.", ex);
        }
    }
}
