using System.Net;
using System.Net.Http.Json;
using ProductCatalog.Application.Products;

namespace ProductCatalog.IntegrationTests.Infrastructure;

/// <summary>Small typed client so tests read like the API contract.</summary>
public sealed class ProductApi(HttpClient http)
{
    private const string Base = "/api/products";

    public HttpClient Http { get; } = http;

    public static CreateProductRequest NewProduct(string? sku = null, int stock = 10, string name = "Test product")
        => new(name, "Created by a test", sku ?? $"TST-{Guid.NewGuid():N}"[..20], 49.90m, stock, CategoryId: 1);

    /// <summary>An update that keeps every detail of <paramref name="loaded"/>, like a form opened on it.</summary>
    public static UpdateProductRequest EditOf(ProductDto loaded)
        => new(loaded.Name, loaded.Description, loaded.Sku, loaded.Price, loaded.CategoryId, loaded.Version);

    public Task<HttpResponseMessage> CreateAsync(CreateProductRequest request) => Http.PostAsJsonAsync(Base, request);

    public async Task<ProductDto> CreateOkAsync(CreateProductRequest request)
    {
        var response = await CreateAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDto>())!;
    }

    public Task<HttpResponseMessage> GetAsync(int id) => Http.GetAsync($"{Base}/{id}");

    public Task<HttpResponseMessage> UpdateAsync(int id, UpdateProductRequest request) => Http.PutAsJsonAsync($"{Base}/{id}", request);

    public Task<HttpResponseMessage> DeleteAsync(int id) => Http.DeleteAsync($"{Base}/{id}");

    public Task<HttpResponseMessage> DecrementAsync(int id, int quantity) => Http.PostAsync($"{Base}/{id}/decrement-stock/{quantity}", null);

    public Task<HttpResponseMessage> AddAsync(int id, int quantity) => Http.PostAsync($"{Base}/{id}/add-to-stock/{quantity}", null);

    public async Task<List<ProductDto>> ListAsync(string path = "")
        => (await Http.GetFromJsonAsync<List<ProductDto>>($"{Base}{path}"))!;
}
