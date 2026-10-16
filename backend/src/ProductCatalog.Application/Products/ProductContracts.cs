namespace ProductCatalog.Application.Products;

/// <summary>
/// Product as returned by every endpoint. Always carries the available stock.
/// <see cref="Version"/> identifies the current product details; send it back when updating them.
/// </summary>
public sealed record ProductDto(
    int Id,
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    int Stock,
    int CategoryId,
    string CategoryName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid Version);

/// <summary>
/// Optional paging and sorting, read from the query string of the list endpoints.
/// Without <see cref="Page"/> and <see cref="PageSize"/> every match is returned, as the original contract requires.
/// </summary>
/// <param name="SortBy">name, categoryName, price or stock. Each endpoint has its own default order.</param>
/// <param name="SortDir">asc (default) or desc; only together with <paramref name="SortBy"/>.</param>
public sealed record ListOptions(int? Page = null, int? PageSize = null, string? SortBy = null, string? SortDir = null)
{
    public static readonly ListOptions None = new();
}

/// <param name="TotalCount">Matches across all pages; the API returns it in the X-Total-Count header.</param>
public sealed record ProductPage(IReadOnlyList<ProductDto> Items, int TotalCount);

/// <summary>Catalogue-wide figures for the dashboard; filters and paging do not affect them.</summary>
public sealed record ProductSummary(int ProductCount, long UnitsInStock, int LowStock, int OutOfStock);

// Numeric fields are nullable so that a field missing from the JSON is reported as required
// instead of silently becoming 0. The validators guarantee they are present before use.
public sealed record CreateProductRequest(
    string Name,
    string? Description,
    string Sku,
    decimal? Price,
    int? Stock,
    int? CategoryId);

/// <summary>
/// Replaces the descriptive data of a product. Stock is deliberately excluded:
/// it only changes through the add-to-stock / decrement-stock operations, so an edit form
/// holding a stale stock value can never overwrite a concurrent stock movement.
/// <see cref="Version"/> is the version the client loaded; if someone else saved in between, the update is a conflict.
/// </summary>
public sealed record UpdateProductRequest(
    string Name,
    string? Description,
    string Sku,
    decimal? Price,
    int? CategoryId,
    Guid? Version);
