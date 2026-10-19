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
        var result = await _service.SearchAsync(term, null, null, ListOptions.None, CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public async Task Search_trims_the_term_and_can_narrow_by_stock()
    {
        ReturnsFromQuery(TestData.Product());

        var result = await _service.SearchAsync("  smart ", 1, 10, ListOptions.None, CancellationToken.None);

        Assert.Single(result.Value.Items);
        await _products.Received(1).QueryAsync(
            new ProductFilter("smart", 1, 10), new ProductOrder(ProductSortColumn.Name), null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, 10, "min")]
    [InlineData(10, 5, "max")]
    public async Task Stock_level_rejects_invalid_ranges(int min, int max, string field)
    {
        var result = await _service.ListByStockLevelAsync(min, max, ListOptions.None, CancellationToken.None);

        Assert.Contains(field, result.Error!.ValidationErrors!.Keys);
    }

    [Fact]
    public async Task Stock_level_starts_at_zero_and_is_ordered_by_stock_by_default()
    {
        ReturnsFromQuery(TestData.Product());

        var result = await _service.ListByStockLevelAsync(null, null, ListOptions.None, CancellationToken.None);

        Assert.Single(result.Value.Items);
        await _products.Received(1).QueryAsync(
            new ProductFilter(MinStock: 0), new ProductOrder(ProductSortColumn.Stock), null, Arg.Any<CancellationToken>());
    }

    // ---- Paging and sorting ----

    [Fact]
    public async Task Without_paging_parameters_every_match_is_returned()
    {
        ReturnsFromQuery(TestData.Product());

        var result = await _service.ListAsync(ListOptions.None, CancellationToken.None);

        Assert.Equal(1, result.Value.TotalCount);
        await _products.Received(1).QueryAsync(Arg.Any<ProductFilter>(), Arg.Any<ProductOrder>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Paging_and_sorting_parameters_reach_the_repository()
    {
        ReturnsFromQuery(TestData.Product());

        await _service.ListAsync(new ListOptions(Page: 3, PageSize: 25, SortBy: "PRICE", SortDir: "desc"), CancellationToken.None);

        await _products.Received(1).QueryAsync(
            new ProductFilter(), new ProductOrder(ProductSortColumn.Price, Descending: true), new PageRequest(3, 25), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Page_size_alone_starts_at_page_one_and_page_alone_uses_the_default_size()
    {
        ReturnsFromQuery(TestData.Product());

        await _service.ListAsync(new ListOptions(PageSize: 5), CancellationToken.None);
        await _service.ListAsync(new ListOptions(Page: 2), CancellationToken.None);

        await _products.Received(1).QueryAsync(Arg.Any<ProductFilter>(), Arg.Any<ProductOrder>(), new PageRequest(1, 5), Arg.Any<CancellationToken>());
        await _products.Received(1).QueryAsync(
            Arg.Any<ProductFilter>(), Arg.Any<ProductOrder>(), new PageRequest(2, ProductService.DefaultPageSize), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 10, null, null, "page")]
    [InlineData(1, 0, null, null, "pageSize")]
    [InlineData(1, 101, null, null, "pageSize")]
    [InlineData(null, null, "colour", null, "sortBy")]
    [InlineData(null, null, "price", "up", "sortDir")]
    [InlineData(null, null, null, "desc", "sortDir")]
    public async Task Invalid_paging_or_sorting_is_a_validation_error_and_queries_nothing(
        int? page, int? pageSize, string? sortBy, string? sortDir, string field)
    {
        var result = await _service.ListAsync(new ListOptions(page, pageSize, sortBy, sortDir), CancellationToken.None);

        Assert.Contains(field, result.Error!.ValidationErrors!.Keys);
        await _products.DidNotReceiveWithAnyArgs().QueryAsync(default!, default!, default, default);
    }

    [Fact]
    public async Task Range_and_paging_errors_are_reported_together()
    {
        var result = await _service.ListByStockLevelAsync(-1, null, new ListOptions(PageSize: 500), CancellationToken.None);

        Assert.Equal(["min", "pageSize"], result.Error!.ValidationErrors!.Keys.Order());
    }

    private void ReturnsFromQuery(params Product[] items)
        => _products.QueryAsync(Arg.Any<ProductFilter>(), Arg.Any<ProductOrder>(), Arg.Any<PageRequest?>(), Arg.Any<CancellationToken>())
            .Returns(new ProductQueryResult(items, items.Length));

    // ---- Update / Delete ----

    [Fact]
    public async Task Update_changes_details_and_keeps_stock()
    {
        var product = TestData.Product(stock: 4);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var loadedVersion = product.Version;

        var result = await _service.UpdateAsync(product.Id, TestData.UpdateRequest(loadedVersion), CancellationToken.None);

        Assert.Equal("Renamed product", result.Value.Name);
        Assert.Equal("Frames", result.Value.CategoryName);
        Assert.Equal(4, result.Value.Stock);
        Assert.NotEqual(loadedVersion, result.Value.Version); // the next edit must send the new version
    }

    [Fact]
    public async Task Update_unknown_product_is_not_found()
    {
        var result = await _service.UpdateAsync(111_111, TestData.UpdateRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task Update_based_on_a_stale_version_is_a_conflict_and_saves_nothing()
    {
        var product = TestData.Product();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _service.UpdateAsync(product.Id, TestData.UpdateRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("product.concurrent_update", result.Error.Code);
        Assert.Equal("SmartLife Progressive", product.Name);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_without_a_version_applies_to_the_current_details()
    {
        var product = TestData.Product();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);

        var result = await _service.UpdateAsync(product.Id, TestData.UpdateRequest(Guid.NewGuid()) with { Version = null }, CancellationToken.None);

        Assert.Equal("Renamed product", result.Value.Name);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_returns_the_stock_as_stored_after_the_save_not_as_loaded()
    {
        var product = TestData.Product(stock: 4);
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        // A sale committed while the update was running; it does not change the version, so the save succeeds.
        _products.GetStockAsync(product.Id, Arg.Any<CancellationToken>()).Returns(1);

        var result = await _service.UpdateAsync(product.Id, TestData.UpdateRequest(product.Version), CancellationToken.None);

        Assert.Equal(1, result.Value.Stock);
    }

    [Fact]
    public async Task Update_that_loses_a_concurrency_race_is_a_conflict()
    {
        var product = TestData.Product();
        _products.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ConcurrencyConflictException("changed"));

        var result = await _service.UpdateAsync(product.Id, TestData.UpdateRequest(product.Version), CancellationToken.None);

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
