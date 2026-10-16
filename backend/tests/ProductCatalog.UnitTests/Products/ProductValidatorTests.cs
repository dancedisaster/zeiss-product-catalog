using ProductCatalog.Application.Products;
using ProductCatalog.UnitTests.TestDoubles;

namespace ProductCatalog.UnitTests.Products;

public class ProductValidatorTests
{
    private readonly CreateProductRequestValidator _create = new();
    private readonly UpdateProductRequestValidator _update = new();

    [Fact]
    public void A_complete_create_request_is_valid()
        => Assert.True(_create.Validate(TestData.CreateRequest()).IsValid);

    [Theory]
    [InlineData("", "LNS-1", 10, 1, 1, "Name")]
    [InlineData("Lens", "", 10, 1, 1, "Sku")]
    [InlineData("Lens", "has spaces", 10, 1, 1, "Sku")]
    [InlineData("Lens", "LNS-1", 0, 1, 1, "Price")]
    [InlineData("Lens", "LNS-1", -5, 1, 1, "Price")]
    [InlineData("Lens", "LNS-1", 10, -1, 1, "Stock")]
    [InlineData("Lens", "LNS-1", 10, 1, 0, "CategoryId")]
    public void Invalid_create_requests_report_the_offending_field(
        string name, string sku, decimal price, int stock, int categoryId, string expectedField)
    {
        var result = _create.Validate(TestData.CreateRequest(name, sku, price, stock, categoryId));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == expectedField);
    }

    [Fact]
    public void An_empty_create_request_reports_each_missing_field_once_as_required()
    {
        // What System.Text.Json produces for "{}".
        var result = _create.Validate(new CreateProductRequest(null!, null, null!, null, null, null));

        Assert.Equal(["CategoryId", "Name", "Price", "Sku", "Stock"], result.Errors.Select(e => e.PropertyName).Order());
        Assert.All(result.Errors, e => Assert.EndsWith("is required.", e.ErrorMessage, StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_stock_is_rejected_instead_of_becoming_zero()
    {
        var result = _create.Validate(TestData.CreateRequest() with { Stock = null });

        Assert.Equal("Stock", Assert.Single(result.Errors).PropertyName);
    }

    [Fact]
    public void Name_longer_than_the_limit_is_rejected()
    {
        var result = _create.Validate(TestData.CreateRequest(name: new string('x', 101)));
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public void Price_with_more_than_two_decimals_is_rejected()
    {
        var result = _create.Validate(TestData.CreateRequest(price: 10.999m));
        Assert.Contains(result.Errors, e => e.PropertyName == "Price");
    }

    [Fact]
    public void A_complete_update_request_is_valid()
        => Assert.True(_update.Validate(TestData.UpdateRequest(Guid.NewGuid())).IsValid);

    [Fact]
    public void Update_request_requires_a_valid_sku()
    {
        var result = _update.Validate(TestData.UpdateRequest(Guid.NewGuid(), sku: "not valid!"));
        Assert.Contains(result.Errors, e => e.PropertyName == "Sku");
    }

    [Fact]
    public void An_empty_update_request_reports_each_missing_field_including_the_version()
    {
        var result = _update.Validate(new UpdateProductRequest(null!, null, null!, null, null, null));

        Assert.Equal(["CategoryId", "Name", "Price", "Sku", "Version"], result.Errors.Select(e => e.PropertyName).Order());
    }
}
