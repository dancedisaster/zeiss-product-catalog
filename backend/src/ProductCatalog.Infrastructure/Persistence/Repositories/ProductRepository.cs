using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Application.Products;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Infrastructure.Persistence.Repositories;

internal sealed class ProductRepository(CatalogDbContext db) : IProductRepository
{
    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken)
        => db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<ProductQueryResult> QueryAsync(
        ProductFilter filter, ProductOrder order, PageRequest? page, CancellationToken cancellationToken)
    {
        var matches = db.Products.AsNoTracking();
        if (filter.Name is not null)
        {
            // EF Core translates Contains to a parameterised LIKE and escapes %, _ and [ in the input,
            // so the search is injection-safe and matches literally. Case-insensitive via the default collation.
            matches = matches.Where(p => p.Name.Contains(filter.Name));
        }

        if (filter.MinStock is { } min)
        {
            matches = matches.Where(p => p.Stock >= min);
        }

        if (filter.MaxStock is { } max)
        {
            matches = matches.Where(p => p.Stock <= max);
        }

        var query = Sort(matches.Include(p => p.Category), order);
        if (page is null)
        {
            var all = await query.ToListAsync(cancellationToken);
            return new ProductQueryResult(all, all.Count);
        }

        var total = await matches.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        return new ProductQueryResult(items, total);
    }

    public async Task<ProductSummary> GetSummaryAsync(CancellationToken cancellationToken)
        // One aggregate query: SELECT COUNT(*), SUM(Stock), COUNT(CASE ...) ... FROM Products.
        => await db.Products
            .GroupBy(_ => 1)
            .Select(all => new ProductSummary(
                all.Count(),
                all.Sum(p => (long)p.Stock),
                all.Count(p => p.Stock > 0 && p.Stock <= Product.LowStockThreshold),
                all.Count(p => p.Stock == 0)))
            .FirstOrDefaultAsync(cancellationToken)
            ?? new ProductSummary(0, 0, 0, 0);

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
        // Version is left alone on purpose: it guards the details, which a stock movement does not touch,
        // so a sale never turns someone's open edit form into a conflict.
        var query = db.Products.Where(p => p.Id == id);
        query = delta < 0
            ? query.Where(p => p.Stock >= -delta)
            : query.Where(p => p.Stock <= int.MaxValue - delta);

        var affected = await query.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(p => p.Stock, p => p.Stock + delta)
                .SetProperty(p => p.UpdatedAt, now),
            cancellationToken);

        return affected == 1;
    }

    public void Add(Product product) => db.Products.Add(product);

    public void Remove(Product product) => db.Products.Remove(product);

    private static IQueryable<Product> Sort(IQueryable<Product> query, ProductOrder order)
    {
        var sorted = order.Column switch
        {
            ProductSortColumn.Name => order.Descending ? query.OrderByDescending(p => p.Name) : query.OrderBy(p => p.Name),
            ProductSortColumn.CategoryName => order.Descending ? query.OrderByDescending(p => p.Category!.Name) : query.OrderBy(p => p.Category!.Name),
            ProductSortColumn.Price => order.Descending ? query.OrderByDescending(p => p.Price) : query.OrderBy(p => p.Price),
            ProductSortColumn.Stock => order.Descending ? query.OrderByDescending(p => p.Stock) : query.OrderBy(p => p.Stock),
            _ => throw new ArgumentOutOfRangeException(nameof(order), order.Column, "Unknown sort column."),
        };

        // Names repeat and prices tie, so finish with the unique id: without a total order,
        // SQL Server may return a row on two pages, or on none.
        return sorted.ThenBy(p => p.Name).ThenBy(p => p.Id);
    }
}
