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
    public async Task InitialiseAsync(bool seedSampleData, CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);

        if (!seedSampleData || await db.Products.AnyAsync(cancellationToken))
        {
            return;
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
        LogSeeded(CatalogSeedData.Products.Length);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} sample products")]
    private partial void LogSeeded(int count);
}
