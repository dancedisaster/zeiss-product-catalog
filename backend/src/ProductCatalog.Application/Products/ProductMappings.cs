using ProductCatalog.Domain.Products;

namespace ProductCatalog.Application.Products;

internal static class ProductMappings
{
    public static ProductDto ToDto(this Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Sku,
        product.Price,
        product.Stock,
        product.CategoryId,
        product.Category?.Name ?? string.Empty,
        product.CreatedAt,
        product.UpdatedAt,
        product.Version);

    public static IReadOnlyList<ProductDto> ToDtos(this IEnumerable<Product> products)
        => products.Select(ToDto).ToList();
}
