using ProductCatalog.Domain.Products;

namespace ProductCatalog.Application.Abstractions;

/// <summary>Which products to return. Null means "no condition" for that field.</summary>
public sealed record ProductFilter(string? Name = null, int? MinStock = null, int? MaxStock = null);

public enum ProductSortColumn
{
    Name,
    CategoryName,
    Price,
    Stock,
}

/// <summary>
/// The main sort column. Repositories break ties by name and then by id, so the order is total
/// and page boundaries never shift between requests.
/// </summary>
public sealed record ProductOrder(ProductSortColumn Column, bool Descending = false);

/// <summary>A 1-based page of results.</summary>
public sealed record PageRequest(int Page, int PageSize)
{
    /// <summary>Rows to skip; capped so an absurd page number yields an empty page instead of an overflow.</summary>
    public int Skip => (int)Math.Min((long)(Page - 1) * PageSize, int.MaxValue);
}

/// <param name="TotalCount">Number of products matching the filter, across all pages.</param>
public sealed record ProductQueryResult(IReadOnlyList<Product> Items, int TotalCount);
