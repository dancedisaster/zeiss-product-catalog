using ProductCatalog.Domain.Categories;

namespace ProductCatalog.Application.Abstractions;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken);
}
