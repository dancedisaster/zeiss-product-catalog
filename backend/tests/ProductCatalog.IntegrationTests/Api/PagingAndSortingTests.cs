using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ProductCatalog.Application.Products;
using ProductCatalog.IntegrationTests.Infrastructure;

namespace ProductCatalog.IntegrationTests.Api;

/// <summary>
/// Runs against the 12 seeded sample products and never changes them, so the expected pages, totals
/// and summary figures are exact.
/// </summary>
public class PagingAndSortingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private const int SeededProducts = 12;
    private readonly HttpClient _http = fixture.Factory.CreateClient();

    [Fact]
    public async Task Without_paging_the_full_list_is_returned_as_before()
    {
        var (products, total) = await GetPageAsync("/api/products");

        Assert.Equal(SeededProducts, products.Count);
        Assert.Equal(SeededProducts, total);
    }

    [Fact]
    public async Task A_page_holds_page_size_products_and_the_header_holds_the_total()
    {
        var (first, total) = await GetPageAsync("/api/products?page=1&pageSize=5");
        var (last, _) = await GetPageAsync("/api/products?page=3&pageSize=5");

        Assert.Equal(5, first.Count);
        Assert.Equal(SeededProducts, total);
        Assert.Equal("Acetate Round Frame", first[0].Name); // default order: by name
        Assert.Equal(2, last.Count);
    }

    [Fact]
    public async Task Walking_through_the_pages_returns_every_product_exactly_once()
    {
        var ids = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            // Sorted by category, where several products tie, so the tie-breaks decide which page a product lands on.
            ids.AddRange((await GetPageAsync($"/api/products?page={page}&pageSize=5&sortBy=categoryName")).Products.Select(p => p.Id));
        }

        Assert.Equal(SeededProducts, ids.Distinct().Count());
        Assert.Equal(SeededProducts, ids.Count);
    }

    [Fact]
    public async Task Sorting_happens_on_the_server_across_all_pages()
    {
        var (products, _) = await GetPageAsync("/api/products?page=1&pageSize=3&sortBy=price&sortDir=desc");

        Assert.Equal("SmartLife Progressive 1.60", products[0].Name); // the most expensive of all, not just of a page
        Assert.Equal(products.Select(p => p.Price).OrderDescending(), products.Select(p => p.Price));
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_still_reports_the_total()
    {
        var (products, total) = await GetPageAsync("/api/products?page=99&pageSize=5");

        Assert.Empty(products);
        Assert.Equal(SeededProducts, total);
    }

    [Theory]
    [InlineData("/api/products?pageSize=101", "pageSize")]
    [InlineData("/api/products?page=0", "page")]
    [InlineData("/api/products?sortBy=colour", "sortBy")]
    [InlineData("/api/products/stock-level?min=0&sortBy=stock&sortDir=sideways", "sortDir")]
    public async Task Invalid_paging_or_sorting_is_a_bad_request(string url, string field)
    {
        var response = await _http.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>();
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Search_can_be_narrowed_by_stock_and_paged()
    {
        // "SmartLife" matches two products: 120 and 250 units.
        var (products, total) = await GetPageAsync("/api/products/search?name=smartlife&min=200&page=1&pageSize=10");

        Assert.Equal("SmartLife Single Vision 1.50", Assert.Single(products).Name);
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task Stock_level_pages_keep_their_default_order_by_stock()
    {
        // Three seeded products have 0 to 10 units: 0, 3 and 8.
        var (products, total) = await GetPageAsync("/api/products/stock-level?min=0&max=10&page=1&pageSize=2");

        Assert.Equal([0, 3], products.Select(p => p.Stock));
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task The_summary_counts_the_whole_catalogue()
    {
        var summary = await _http.GetFromJsonAsync<ProductSummary>("/api/products/summary");

        Assert.Equal(new ProductSummary(ProductCount: SeededProducts, UnitsInStock: 3_423, LowStock: 2, OutOfStock: 1), summary);
    }

    private async Task<(List<ProductDto> Products, int Total)> GetPageAsync(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var products = (await response.Content.ReadFromJsonAsync<List<ProductDto>>())!;
        return (products, int.Parse(response.Headers.GetValues("X-Total-Count").Single(), CultureInfo.InvariantCulture));
    }

    private sealed record ValidationProblem(Dictionary<string, string[]> Errors);
}
