using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Infrastructure.Persistence;

namespace ProductCatalog.Infrastructure.IdGeneration;

/// <summary>
/// Takes the next value from a SQL Server SEQUENCE (100000..999999, no cycle).
/// NEXT VALUE FOR is atomic in the database, so any number of API instances can call it
/// concurrently without ever receiving the same id, and without locks in our code.
/// </summary>
internal sealed class SqlSequenceProductIdGenerator(CatalogDbContext db) : IProductIdGenerator
{
    private const string Sql =
        $"SELECT NEXT VALUE FOR [{CatalogDbContext.Schema}].[{CatalogDbContext.ProductIdSequence}] AS [Value]";

    public async Task<int> NextIdAsync(CancellationToken cancellationToken)
    {
        // ToListAsync (not SingleAsync) so EF does not wrap the statement in a sub-query,
        // which SQL Server does not allow for NEXT VALUE FOR.
        var values = await db.Database.SqlQueryRaw<int>(Sql).ToListAsync(cancellationToken);
        return values.Single();
    }
}
