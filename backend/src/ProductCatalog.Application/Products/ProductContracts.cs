namespace ProductCatalog.Application.Products;

/// <summary>Product as returned by every endpoint. Always carries the available stock.</summary>
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
    DateTimeOffset UpdatedAt);

public sealed record CreateProductRequest(
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    int Stock,
    int CategoryId);

/// <summary>
/// Replaces the descriptive data of a product. Stock is deliberately excluded:
/// it only changes through the add-to-stock / decrement-stock operations, so an edit form
/// holding a stale stock value can never overwrite a concurrent stock movement.
/// </summary>
public sealed record UpdateProductRequest(
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    int CategoryId);
