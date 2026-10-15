using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(CatalogDbContext db) : IProductRepository
{
    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken)
        => db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken)
        => await ReadOnly().OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> SearchByNameAsync(string name, CancellationToken cancellationToken)
        // EF Core translates Contains to a parameterised LIKE and escapes %, _ and [ in the input,
        // so the search is injection-safe and matches literally. Case-insensitive via the default collation.
        => await ReadOnly().Where(p => p.Name.Contains(name)).OrderBy(p => p.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> ListByStockRangeAsync(int min, int max, CancellationToken cancellationToken)
        => await ReadOnly()
            .Where(p => p.Stock >= min && p.Stock <= max)
            .OrderBy(p => p.Stock)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> SkuExistsAsync(string sku, int? excludingProductId, CancellationToken cancellationToken)
    {
        var normalised = sku.Trim().ToUpperInvariant();
        return db.Products.AnyAsync(
            p => p.Sku == normalised && (excludingProductId == null || p.Id != excludingProductId),
            cancellationToken);
    }

    public async Task<bool> TryChangeStockAsync(int id, int delta, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // UPDATE ... SET Stock = Stock + @delta WHERE Id = @id AND <result in range>
        // The row lock taken by the UPDATE serialises concurrent movements on the same product.
        var query = db.Products.Where(p => p.Id == id);
        query = delta < 0
            ? query.Where(p => p.Stock >= -delta)
            : query.Where(p => p.Stock <= int.MaxValue - delta);

        var version = Guid.NewGuid();
        var affected = await query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(p => p.Stock, p => p.Stock + delta)
                .SetProperty(p => p.UpdatedAt, now)
                .SetProperty(p => p.Version, version),
            cancellationToken);

        return affected == 1;
    }

    public void Add(Product product) => db.Products.Add(product);

    public void Remove(Product product) => db.Products.Remove(product);

    private IQueryable<Product> ReadOnly() => db.Products.AsNoTracking().Include(p => p.Category);
}
