using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Infrastructure.Persistence;

/// <summary>Applies pending migrations and inserts sample products into an empty database.</summary>
public sealed partial class DatabaseInitializer(
    CatalogDbContext db,
    IProductIdGenerator idGenerator,
    TimeProvider clock,
    ILogger<DatabaseInitializer> logger)
{
    // Exclusive application lock, held until the transaction ends. sp_getapplock reports failure (e.g. a timeout)
    // through its return value instead of an error, so turn a negative result into one.
    private const string AcquireSeedLock = """
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = N'ProductCatalog.Seed', @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 60000;
        IF @result < 0 THROW 50000, 'Could not acquire the seeding lock.', 1;
        """;

    // SqlClient abandons a command after 30 s by default, which would end the wait before the 60 s @LockTimeout does:
    // the instance would crash at start-up instead of waiting. The command gets longer than the lock.
    private static readonly TimeSpan SeedLockCommandTimeout = TimeSpan.FromSeconds(90);

    public async Task InitialiseAsync(bool seedSampleData, CancellationToken cancellationToken = default)
    {
        // EF Core takes its own lock while migrating, once the database exists. Creating the database is not
        // coordinated, so instances must not start together on a server where it does not exist yet (see DECISIONS 13).
        await db.Database.MigrateAsync(cancellationToken);

        if (seedSampleData)
        {
            // The retrying execution strategy requires user transactions to run inside it.
            await db.Database.CreateExecutionStrategy().ExecuteAsync(() => SeedOnceAsync(cancellationToken));
        }
    }

    /// <summary>
    /// "Is it empty? Then insert" must not interleave between instances starting together: both would find no
    /// products and the second would fail on the unique SKUs. Under the lock the second waits, then finds them.
    /// </summary>
    private async Task SeedOnceAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear(); // a retried attempt must not re-insert what a failed one added
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var commandTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(SeedLockCommandTimeout);
        try
        {
            await db.Database.ExecuteSqlRawAsync(AcquireSeedLock, cancellationToken);
        }
        finally
        {
            db.Database.SetCommandTimeout(commandTimeout);
        }

        if (await db.Products.AnyAsync(cancellationToken))
        {
            return; // already seeded; disposing the transaction releases the lock
        }

        var categories = await db.Categories.ToDictionaryAsync(c => c.Id, cancellationToken);
        var now = clock.GetUtcNow();

        foreach (var sample in CatalogSeedData.Products)
        {
            var id = await idGenerator.NextIdAsync(cancellationToken);
            db.Products.Add(Product.Create(
                id, sample.Name, sample.Description, sample.Sku, sample.Price, sample.Stock, categories[sample.CategoryId], now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        LogSeeded(CatalogSeedData.Products.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} sample products")]
    private partial void LogSeeded(int count);
}
