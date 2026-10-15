using ProductCatalog.Domain.Categories;
using ProductCatalog.Domain.Common;

namespace ProductCatalog.Domain.Products;

/// <summary>
/// Product aggregate root. Details change only through its methods, so invariants
/// (6-digit id, positive price, required name/SKU...) can never be bypassed.
/// Stock movements are applied as single atomic SQL statements (see IProductRepository.TryChangeStockAsync)
/// because they are hot, concurrent counters; the database CHECK constraint guards "stock >= 0".
/// </summary>
public sealed class Product
{
    public const int MinId = 100_000;
    public const int MaxId = 999_999;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1_000;
    public const int SkuMaxLength = 32;

    // Required by EF Core to materialise entities.
    private Product()
    {
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public int Stock { get; private set; }
    public int CategoryId { get; private set; }
    public Category? Category { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, renewed on every change.</summary>
    public Guid Version { get; private set; }

    public static Product Create(
        int id,
        string name,
        string? description,
        string sku,
        decimal price,
        int initialStock,
        Category category,
        DateTimeOffset now)
    {
        if (id is < MinId or > MaxId)
        {
            throw new DomainException("product.invalid_id", $"Product id must be a 6-digit number, got {id}.");
        }

        if (initialStock < 0)
        {
            throw new DomainException("product.negative_stock", "Initial stock cannot be negative.");
        }

        var product = new Product
        {
            Id = id,
            Stock = initialStock,
            CreatedAt = now,
        };
        product.SetDetails(name, description, sku, price, category, now);
        return product;
    }

    public void UpdateDetails(string name, string? description, string sku, decimal price, Category category, DateTimeOffset now)
        => SetDetails(name, description, sku, price, category, now);

    private void SetDetails(string name, string? description, string sku, decimal price, Category category, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("product.name_required", "Product name is required.");
        }

        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainException("product.sku_required", "Product SKU is required.");
        }

        if (price <= 0)
        {
            throw new DomainException("product.invalid_price", "Price must be greater than zero.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Sku = sku.Trim().ToUpperInvariant();
        Price = price;
        Category = category;
        CategoryId = category.Id;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
