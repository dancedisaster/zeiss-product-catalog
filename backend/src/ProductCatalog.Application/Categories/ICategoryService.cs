using ProductCatalog.Application.Abstractions;

namespace ProductCatalog.Application.Categories;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken);
}

internal sealed class CategoryService(ICategoryRepository categories) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken)
        => (await categories.ListAsync(cancellationToken)).Select(c => new CategoryDto(c.Id, c.Name)).ToList();
}
