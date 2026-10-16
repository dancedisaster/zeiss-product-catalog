using ProductCatalog.Application.Products;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Application.Abstractions;

public interface IProductRepository
{
    /// <summary>Loads a product for modification (tracked), including its category.</summary>
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>
    /// Products matching <paramref name="filter"/> in the given order, with their category.
    /// Without <paramref name="page"/> every match is returned.
    /// </summary>
    Task<ProductQueryResult> QueryAsync(ProductFilter filter, ProductOrder order, PageRequest? page, CancellationToken cancellationToken);

    /// <summary>Catalogue-wide counts for the dashboard, computed by the database in one query.</summary>
    Task<ProductSummary> GetSummaryAsync(CancellationToken cancellationToken);

    Task<bool> SkuExistsAsync(string sku, int? excludingProductId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically adds <paramref name="delta"/> (negative to remove) to the stock, only if the result
    /// stays between 0 and int.MaxValue. Returns false when the product does not exist or the
    /// condition fails. One conditional UPDATE: safe across instances, no lost updates, no retries.
    /// </summary>
    Task<bool> TryChangeStockAsync(int id, int delta, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(Product product);

    void Remove(Product product);
}
