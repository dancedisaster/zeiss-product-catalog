using Microsoft.EntityFrameworkCore;
using ProductCatalog.Domain.Categories;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Infrastructure.Persistence;

public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";
    public const string ProductIdSequence = "ProductIds";

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // One sequence shared by every API instance: SQL Server hands out each value exactly once,
        // which is what makes the 6-digit ids unique in a scaled-out deployment.
        modelBuilder.HasSequence<int>(ProductIdSequence)
            .StartsAt(Product.MinId)
            .IncrementsBy(1)
            .HasMin(Product.MinId)
            .HasMax(Product.MaxId)
            .IsCyclic(false);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
