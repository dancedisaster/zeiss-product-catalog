using ProductCatalog.Domain.Common;
using ProductCatalog.Domain.Products;
using ProductCatalog.UnitTests.TestDoubles;

namespace ProductCatalog.UnitTests.Domain;

public class ProductTests
{
    [Fact]
    public void Create_with_valid_data_sets_all_fields()
    {
        var product = Product.Create(100_000, "  ClearView  ", "  Thin lens ", " lns-cv-01 ", 159m, 5, TestData.Lenses, TestData.Now);

        Assert.Equal(100_000, product.Id);
        Assert.Equal("ClearView", product.Name);
        Assert.Equal("Thin lens", product.Description);
        Assert.Equal("LNS-CV-01", product.Sku);
        Assert.Equal(159m, product.Price);
        Assert.Equal(5, product.Stock);
        Assert.Equal(TestData.Lenses.Id, product.CategoryId);
        Assert.Same(TestData.Lenses, product.Category);
        Assert.Equal(TestData.Now, product.CreatedAt);
        Assert.Equal(TestData.Now, product.UpdatedAt);
        Assert.NotEqual(Guid.Empty, product.Version);
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(1_000_000)]
    [InlineData(0)]
    [InlineData(-123_456)]
    public void Create_rejects_ids_that_are_not_six_digits(int id)
    {
        var ex = Assert.Throws<DomainException>(() => TestData.Product(id: id));
        Assert.Equal("product.invalid_id", ex.Code);
    }

    [Theory]
    [InlineData(Product.MinId)]
    [InlineData(Product.MaxId)]
    public void Create_accepts_the_boundaries_of_the_id_range(int id)
        => Assert.Equal(id, TestData.Product(id: id).Id);

    [Fact]
    public void Create_rejects_negative_stock()
    {
        var ex = Assert.Throws<DomainException>(() => TestData.Product(stock: -1));
        Assert.Equal("product.negative_stock", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Create_rejects_non_positive_price(decimal price)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create(123_456, "Name", null, "SKU-1", price, 1, TestData.Lenses, TestData.Now));
        Assert.Equal("product.invalid_price", ex.Code);
    }

    [Theory]
    [InlineData("", "SKU-1", "product.name_required")]
    [InlineData("   ", "SKU-1", "product.name_required")]
    [InlineData("Name", " ", "product.sku_required")]
    public void Create_rejects_missing_name_or_sku(string name, string sku, string expectedCode)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create(123_456, name, null, sku, 10m, 1, TestData.Lenses, TestData.Now));
        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public void Blank_description_is_stored_as_null()
    {
        var product = Product.Create(123_456, "Name", "   ", "SKU-1", 10m, 1, TestData.Lenses, TestData.Now);
        Assert.Null(product.Description);
    }

    [Fact]
    public void UpdateDetails_changes_details_but_not_stock_or_creation_date()
    {
        var product = TestData.Product(stock: 7);
        var originalVersion = product.Version;
        var later = TestData.Now.AddHours(1);

        product.UpdateDetails("New name", null, "frm-002", 99.5m, TestData.Frames, later);

        Assert.Equal("New name", product.Name);
        Assert.Equal("FRM-002", product.Sku);
        Assert.Equal(99.5m, product.Price);
        Assert.Equal(TestData.Frames.Id, product.CategoryId);
        Assert.Equal(7, product.Stock);
        Assert.Equal(TestData.Now, product.CreatedAt);
        Assert.Equal(later, product.UpdatedAt);
        Assert.NotEqual(originalVersion, product.Version);
    }
}
