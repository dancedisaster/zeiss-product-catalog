using ProductCatalog.Application.Common;

namespace ProductCatalog.Application.Products;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken cancellationToken);

    Task<Result<ProductDto>> GetAsync(int id, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ProductDto>>> SearchAsync(string? name, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ProductDto>>> ListByStockLevelAsync(int? min, int? max, CancellationToken cancellationToken);

    Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);

    Task<Result<ProductDto>> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(int id, CancellationToken cancellationToken);

    Task<Result<ProductDto>> AddStockAsync(int id, int quantity, CancellationToken cancellationToken);

    Task<Result<ProductDto>> DecrementStockAsync(int id, int quantity, CancellationToken cancellationToken);
}
