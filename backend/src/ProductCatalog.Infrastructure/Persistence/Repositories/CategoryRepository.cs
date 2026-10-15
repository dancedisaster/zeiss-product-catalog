using Microsoft.EntityFrameworkCore;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Domain.Categories;

namespace ProductCatalog.Infrastructure.Persistence.Repositories;

internal sealed class CategoryRepository(CatalogDbContext db) : ICategoryRepository
{
    public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken)
        => db.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken)
        => await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken);
}
