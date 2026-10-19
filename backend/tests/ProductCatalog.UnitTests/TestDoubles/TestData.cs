using ProductCatalog.Application.Products;
using ProductCatalog.Domain.Categories;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.UnitTests.TestDoubles;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    public static readonly Category Lenses = new(1, "Spectacle Lenses");

    public static readonly Category Frames = new(3, "Frames");

    public static Product Product(int id = 123_456, int stock = 10, string sku = "LNS-001")
        => ProductCatalog.Domain.Products.Product.Create(id, "SmartLife Progressive", "Progressive lens", sku, 289m, stock, Lenses, Now);

    public static CreateProductRequest CreateRequest(
        string name = "SmartLife Progressive",
        string sku = "LNS-001",
        decimal price = 289m,
        int stock = 10,
        int categoryId = 1)
        => new(name, "Progressive lens", sku, price, stock, categoryId);

    /// <param name="version">The version the client loaded, normally <c>product.Version</c>.</param>
    public static UpdateProductRequest UpdateRequest(Guid version, string sku = "LNS-001", int categoryId = 3)
        => new("Renamed product", "New description", sku, 199.99m, categoryId, version);
}
