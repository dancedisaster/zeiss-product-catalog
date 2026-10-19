using ProductCatalog.Application.Common;

namespace ProductCatalog.Application.Products;

public interface IProductService
{
    Task<Result<ProductPage>> ListAsync(ListOptions options, CancellationToken cancellationToken);

    Task<Result<ProductDto>> GetAsync(int id, CancellationToken cancellationToken);

    Task<Result<ProductPage>> SearchAsync(string? name, int? min, int? max, ListOptions options, CancellationToken cancellationToken);

    Task<Result<ProductPage>> ListByStockLevelAsync(int? min, int? max, ListOptions options, CancellationToken cancellationToken);

    Task<ProductSummary> GetSummaryAsync(CancellationToken cancellationToken);

    Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    Task<Result<ProductDto>> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<Result<ProductDto>> AddStockAsync(int id, int quantity, CancellationToken cancellationToken);

    Task<Result<ProductDto>> DecrementStockAsync(int id, int quantity, CancellationToken cancellationToken);
}
