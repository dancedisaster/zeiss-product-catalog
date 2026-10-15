using ProductCatalog.Domain.Products;

namespace ProductCatalog.Application.Abstractions;

public interface IProductRepository
{
    /// <summary>Loads a product for modification (tracked), including its category.</summary>
    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> SearchByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> ListByStockRangeAsync(int min, int max, CancellationToken cancellationToken);

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
