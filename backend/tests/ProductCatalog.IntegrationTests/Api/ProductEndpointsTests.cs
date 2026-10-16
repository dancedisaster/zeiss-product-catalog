using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using ProductCatalog.Application.Products;
using ProductCatalog.IntegrationTests.Infrastructure;

namespace ProductCatalog.IntegrationTests.Api;

/// <summary>Exercises all 9 required endpoints over HTTP against a real SQL Server.</summary>
public class ProductEndpointsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private readonly ProductApi _api = new(fixture.Factory.CreateClient());

    [Fact]
    public async Task Get_all_returns_the_seeded_products_with_stock()
    {
        var products = await _api.ListAsync();

        Assert.True(products.Count >= 12);
        Assert.All(products, p => Assert.InRange(p.Id, 100_000, 999_999));
        Assert.Contains(products, p => p.Sku == "LNS-SLP-160" && p.Stock == 120);
    }

    [Fact]
    public async Task Post_creates_a_product_with_a_six_digit_id_and_a_location_header()
    {
        var response = await _api.CreateAsync(ProductApi.NewProduct(stock: 7));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProductDto>();
        Assert.InRange(created!.Id, 100_000, 999_999);
        Assert.Equal(7, created.Stock);
        Assert.Equal($"/api/products/{created.Id}", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Post_with_invalid_body_returns_problem_details_with_field_errors()
    {
        var response = await _api.CreateAsync(new CreateProductRequest("", null, "bad sku", -1m, -1, 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Equal(["name", "price", "sku", "stock"], problem!.Errors.Keys.Order());
    }

    [Fact]
    public async Task Post_with_an_empty_body_reports_every_required_field()
    {
        var response = await _api.Http.PostAsJsonAsync("/api/products", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Equal(["categoryId", "name", "price", "sku", "stock"], problem!.Errors.Keys.Order());
        Assert.All(problem.Errors.Values, messages => Assert.EndsWith("is required.", Assert.Single(messages), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_without_stock_is_rejected_instead_of_defaulting_to_zero()
    {
        var response = await _api.Http.PostAsJsonAsync("/api/products", new { name = "No stock given", sku = "NO-STOCK-1", price = 10m, categoryId = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Equal(["stock"], problem!.Errors.Keys);
    }

    [Fact]
    public async Task Post_with_unknown_category_is_rejected()
    {
        var response = await _api.CreateAsync(ProductApi.NewProduct() with { CategoryId = 999 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_duplicate_sku_is_a_conflict()
    {
        var request = ProductApi.NewProduct();
        await _api.CreateOkAsync(request);

        var response = await _api.CreateAsync(request with { Sku = request.Sku.ToLowerInvariant() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Get_by_id_returns_the_product_or_404()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct());

        var found = await (await _api.GetAsync(created.Id)).Content.ReadFromJsonAsync<ProductDto>();
        var missing = await _api.GetAsync(999_999);

        Assert.Equal(created.Sku, found!.Sku);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Put_updates_details_and_keeps_stock()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct(stock: 9));

        var response = await _api.UpdateAsync(created.Id, new UpdateProductRequest("Renamed", null, created.Sku, 10m, 3, created.Version));
        var updated = await response.Content.ReadFromJsonAsync<ProductDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Renamed", updated!.Name);
        Assert.Equal("Frames", updated.CategoryName);
        Assert.Equal(9, updated.Stock);
        Assert.NotEqual(created.Version, updated.Version);
    }

    [Fact]
    public async Task Put_on_missing_product_is_404()
    {
        var response = await _api.UpdateAsync(999_998, new UpdateProductRequest("X", null, "SKU-X", 10m, 1, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Put_without_the_loaded_version_is_a_bad_request()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct());

        var response = await _api.UpdateAsync(created.Id, ProductApi.EditOf(created) with { Version = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Equal(["version"], problem!.Errors.Keys);
    }

    [Fact]
    public async Task Put_based_on_a_stale_version_is_a_conflict_and_keeps_the_other_edit()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct());
        // Two users open the same product; the first one saves.
        Assert.Equal(HttpStatusCode.OK, (await _api.UpdateAsync(created.Id, ProductApi.EditOf(created) with { Name = "First edit" })).StatusCode);

        var second = await _api.UpdateAsync(created.Id, ProductApi.EditOf(created) with { Name = "Second edit" });
        var current = await (await _api.GetAsync(created.Id)).Content.ReadFromJsonAsync<ProductDto>();

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("product.concurrent_update", (await second.Content.ReadFromJsonAsync<CodedProblem>())!.Code);
        Assert.Equal("First edit", current!.Name);
    }

    [Fact]
    public async Task A_stock_movement_does_not_invalidate_an_open_edit()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct(stock: 5));
        // A sale happens while the edit form is open.
        Assert.Equal(HttpStatusCode.OK, (await _api.DecrementAsync(created.Id, 2)).StatusCode);

        var response = await _api.UpdateAsync(created.Id, ProductApi.EditOf(created) with { Name = "Edited during a sale" });
        var updated = await response.Content.ReadFromJsonAsync<ProductDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Edited during a sale", updated!.Name);
        Assert.Equal(3, updated.Stock);
    }

    [Fact]
    public async Task Delete_removes_the_product()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct());

        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync(created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.GetAsync(created.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.DeleteAsync(created.Id)).StatusCode);
    }

    [Fact]
    public async Task Decrement_stock_returns_ok_with_the_new_stock()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct(stock: 10));

        var response = await _api.DecrementAsync(created.Id, 4);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, (await response.Content.ReadFromJsonAsync<ProductDto>())!.Stock);
    }

    [Fact]
    public async Task Decrement_below_zero_is_a_conflict_and_leaves_stock_untouched()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct(stock: 2));

        var response = await _api.DecrementAsync(created.Id, 3);
        var after = await (await _api.GetAsync(created.Id)).Content.ReadFromJsonAsync<ProductDto>();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, after!.Stock);
    }

    [Fact]
    public async Task Add_to_stock_returns_ok_with_the_new_stock()
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct(stock: 1));

        var response = await _api.AddAsync(created.Id, 99);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, (await response.Content.ReadFromJsonAsync<ProductDto>())!.Stock);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Stock_endpoints_reject_non_positive_quantities(int quantity)
    {
        var created = await _api.CreateOkAsync(ProductApi.NewProduct());

        Assert.Equal(HttpStatusCode.BadRequest, (await _api.AddAsync(created.Id, quantity)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.DecrementAsync(created.Id, quantity)).StatusCode);
    }

    [Fact]
    public async Task Stock_endpoints_on_missing_product_are_404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _api.AddAsync(999_997, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.DecrementAsync(999_997, 1)).StatusCode);
    }

    [Fact]
    public async Task Search_matches_partial_names_case_insensitively()
    {
        var products = await _api.ListAsync("/search?name=sMaRtLiFe");

        Assert.Equal(2, products.Count);
        Assert.All(products, p => Assert.Contains("SmartLife", p.Name, StringComparison.Ordinal));
        Assert.All(products, p => Assert.True(p.Stock > 0));
    }

    [Fact]
    public async Task Search_treats_sql_wildcards_literally()
    {
        Assert.Empty(await _api.ListAsync("/search?name=%25"));
        Assert.Empty(await _api.ListAsync("/search?name=_"));
    }

    [Fact]
    public async Task Search_without_a_term_is_a_bad_request()
    {
        var response = await _api.Http.GetAsync("/api/products/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Stock_level_returns_only_products_inside_the_range()
    {
        var products = await _api.ListAsync("/stock-level?min=1&max=10");

        Assert.NotEmpty(products);
        Assert.All(products, p => Assert.InRange(p.Stock, 1, 10));
    }

    [Fact]
    public async Task Stock_level_with_inverted_range_is_a_bad_request()
    {
        var response = await _api.Http.GetAsync("/api/products/stock-level?min=10&max=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await _api.Http.GetAsync("/api/products");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);

    private sealed record CodedProblem(string Code);
}
