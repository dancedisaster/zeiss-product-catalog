using System.Net.Http.Json;
using ProductCatalog.Application.Products;
using ProductCatalog.IntegrationTests.Infrastructure;

namespace ProductCatalog.IntegrationTests.Api;

/// <summary>
/// Proves the two concurrency requirements with real parallel traffic:
/// ids stay unique across several API instances, and stock never goes negative.
/// </summary>
public class DistributedIdGenerationTests
{
    [Fact]
    public async Task Two_api_instances_creating_in_parallel_never_produce_duplicate_ids()
    {
        var connectionString = await SqlServerContainer.CreateDatabaseConnectionStringAsync();
        await using var instanceA = new CatalogApiFactory(connectionString, seedSampleData: false);
        await using var instanceB = new CatalogApiFactory(connectionString, seedSampleData: false);
        _ = instanceA.Server; // migrate once before both instances take traffic
        var clients = new[] { instanceA.CreateClient(), instanceB.CreateClient() };

        var creations = Enumerable.Range(0, 100).Select(async i =>
        {
            var api = new ProductApi(clients[i % 2]);
            return (await api.CreateOkAsync(ProductApi.NewProduct(sku: $"PAR-{i}"))).Id;
        });
        var ids = await Task.WhenAll(creations);

        Assert.Equal(100, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, 100_000, 999_999));
    }

    [Fact]
    public async Task Concurrent_decrements_from_two_instances_never_oversell()
    {
        var connectionString = await SqlServerContainer.CreateDatabaseConnectionStringAsync();
        await using var instanceA = new CatalogApiFactory(connectionString, seedSampleData: false);
        await using var instanceB = new CatalogApiFactory(connectionString, seedSampleData: false);
        _ = instanceA.Server;
        var clients = new[] { instanceA.CreateClient(), instanceB.CreateClient() };
        var product = await new ProductApi(clients[0]).CreateOkAsync(ProductApi.NewProduct(stock: 25));

        var attempts = Enumerable.Range(0, 60).Select(i => new ProductApi(clients[i % 2]).DecrementAsync(product.Id, 1));
        var responses = await Task.WhenAll(attempts);

        Assert.Equal(25, responses.Count(r => r.IsSuccessStatusCode));
        var final = await clients[1].GetFromJsonAsync<ProductDto>($"/api/products/{product.Id}");
        Assert.Equal(0, final!.Stock);
    }
}
