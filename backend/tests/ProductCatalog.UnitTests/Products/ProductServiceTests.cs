using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Application.Common;
using ProductCatalog.Application.Products;
using ProductCatalog.Domain.Products;
using ProductCatalog.UnitTests.TestDoubles;

namespace ProductCatalog.UnitTests.Products;

public class ProductServiceTests
{
    private readonly IProductRepository _products = Substitute.For<IProductRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IProductIdGenerator _ids = Substitute.For<IProductIdGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedTimeProvider _clock = new(TestData.Now);
    private readonly ProductService _service;

    public ProductServiceTests()
    {
        _categories.GetByIdAsync(TestData.Lenses.Id, Arg.Any<CancellationToken>()).Returns(TestData.Lenses);
        _categories.GetByIdAsync(TestData.Frames.Id, Arg.Any<CancellationToken>()).Returns(TestData.Frames);
        _ids.NextIdAsync(Arg.Any<CancellationToken>()).Returns(654_321);

        _service = new ProductService(
            _products,
            _categories,
            _ids,
            _unitOfWork,
            new CreateProductRequestValidator(),
            new UpdateProductRequestValidator(),
            _clock,
            NullLogger<ProductService>.Instance);
    }

    // ---- Create ----

    [Fact]
    public async Task Create_uses_the_generated_id_and_persists_the_product()
    {
        var result = await _service.CreateAsync(TestData.CreateRequest(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(654_321, result.Value.Id);
        Assert.Equal(10, result.Value.Stock);
        Assert.Equal("Spectacle Lenses", result.Value.CategoryName);
        _products.Received(1).Add(Arg.Is<Product>(p => p.Id == 654_321));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_with_invalid_input_returns_validation_errors_and_saves_nothing()
    {
        var result = await _service.CreateAsync(TestData.CreateRequest(name: "", price: -1), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains("name", result.Error.ValidationErrors!.Keys);
        Assert.Contains("price", result.Error.ValidationErrors!.Keys);
        await _ids.DidNotReceive().NextIdAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_with_unknown_category_is_a_validation_error()
    {
        var result = await _service.CreateAsync(TestData.CreateRequest(categoryId: 42), CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains("categoryId", result.Error.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Create_with_existing_sku_is_a_conflict()
    {
        _products.SkuExistsAsync("LNS-001", null, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.CreateAsync(TestData.CreateRequest(), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("product.duplicate_sku", result.Error.Code);
    }

    [Fact]
    public async Task Create_racing_another_request_for_the_same_sku_is_a_conflict()
    {
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new UniqueConstraintViolationException("duplicate"));

        var result = await _service.CreateAsync(TestData.CreateRequest(), CancellationToken.None);

        Assert.Equal("product.duplicate_sku", result.Error!.Code);
    }

    // ---- Read ----

    [Fact]
    public async Task Get_unknown_product_is_not_found()
    {
        var result = await _service.GetAsync(111_111, CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Get_returns_the_product_with_its_stock()
    {
        _products.GetByIdAsync(123_456, Arg.Any<CancellationToken>()).Returns(TestData.Product(stock: 3));

        var result = await _service.GetAsync(123_456, CancellationToken.None);

        Assert.Equal(3, result.Value.Stock);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_requires_a_term(string? term)
    {
        var result = await _service.SearchAsync(term, CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Search_trims_the_term_before_querying()
    {
        _products.SearchByNameAsync("smart", Arg.Any<CancellationToken>()).Returns([TestData.Product()]);

        var result = await _service.SearchAsync("  smart ", CancellationToken.None);

        Assert.Single(result.Value);
    }

    [Theory]
    [InlineData(-1, 10, "min")]
    [InlineData(10, 5, "max")]
    public async Task Stock_level_rejects_invalid_ranges(int min, int max, string field)
    {
        var result = await _service.ListByStockLevelAsync(min, max, CancellationToken.None);

        Assert.Contains(field, result.Error!.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Stock_level_defaults_to_the_full_range_when_bounds_are_missing()
    {
        _products.ListByStockRangeAsync(0, int.MaxValue, Arg.Any<CancellationToken>()).Returns([TestData.Product()]);

        var result = await _service.ListByStockLevelAsync(null, null, CancellationToken.None);

        Assert.Single(result.Value);
    }

    // ---- Update / Delete ----

    [Fact]
    public async Task Update_changes_details_and_keeps_stock()
    {
        var product = TestData.Product(stock: 4);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _service.UpdateAsync(product.Id, TestData.UpdateRequest(), CancellationToken.None);

        Assert.Equal("Renamed product", result.Value.Name);
        Assert.Equal("Frames", result.Value.CategoryName);
        Assert.Equal(4, result.Value.Stock);
    }

    [Fact]
    public async Task Update_unknown_product_is_not_found()
    {
        var result = await _service.UpdateAsync(111_111, TestData.UpdateRequest(), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Update_that_loses_a_concurrency_race_is_a_conflict()
    {
        var product = TestData.Product();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("changed"));

        var result = await _service.UpdateAsync(product.Id, TestData.UpdateRequest(), CancellationToken.None);

        Assert.Equal("product.concurrent_update", result.Error!.Code);
    }

    [Fact]
    public async Task Delete_removes_an_existing_product()
    {
        var product = TestData.Product();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _service.DeleteAsync(product.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _products.Received(1).Remove(product);
    }

    [Fact]
    public async Task Delete_unknown_product_is_not_found()
    {
        var result = await _service.DeleteAsync(111_111, CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    // ---- Stock ----

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Stock_changes_require_a_positive_quantity(int quantity)
    {
        var decrement = await _service.DecrementStockAsync(123_456, quantity, CancellationToken.None);
        var add = await _service.AddStockAsync(123_456, quantity, CancellationToken.None);

        Assert.Equal(ErrorType.Validation, decrement.Error!.Type);
        Assert.Equal(ErrorType.Validation, add.Error!.Type);
        await _products.DidNotReceiveWithAnyArgs().TryChangeStockAsync(default, default, default, default);
    }

    [Fact]
    public async Task Decrement_applies_a_negative_delta_and_returns_the_new_stock()
    {
        _products.TryChangeStockAsync(123_456, -3, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);
        _products.GetByIdAsync(123_456, Arg.Any<CancellationToken>()).Returns(TestData.Product(stock: 7));

        var result = await _service.DecrementStockAsync(123_456, 3, CancellationToken.None);

        Assert.Equal(7, result.Value.Stock);
    }

    [Fact]
    public async Task Add_applies_a_positive_delta()
    {
        _products.TryChangeStockAsync(123_456, 5, TestData.Now, Arg.Any<CancellationToken>()).Returns(true);
        _products.GetByIdAsync(123_456, Arg.Any<CancellationToken>()).Returns(TestData.Product(stock: 15));

        var result = await _service.AddStockAsync(123_456, 5, CancellationToken.None);

        Assert.Equal(15, result.Value.Stock);
    }

    [Fact]
    public async Task Decrement_beyond_available_stock_is_a_conflict_that_reports_the_stock()
    {
        _products.TryChangeStockAsync(123_456, -20, TestData.Now, Arg.Any<CancellationToken>()).Returns(false);
        _products.GetByIdAsync(123_456, Arg.Any<CancellationToken>()).Returns(TestData.Product(stock: 2));

        var result = await _service.DecrementStockAsync(123_456, 20, CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("product.insufficient_stock", result.Error.Code);
        Assert.Contains("2 unit(s)", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stock_change_on_unknown_product_is_not_found()
    {
        var result = await _service.AddStockAsync(111_111, 1, CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
