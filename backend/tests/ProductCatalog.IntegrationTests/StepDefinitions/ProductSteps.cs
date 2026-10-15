using System.Net;
using System.Net.Http.Json;
using ProductCatalog.Application.Products;
using ProductCatalog.IntegrationTests.Infrastructure;
using Reqnroll;

namespace ProductCatalog.IntegrationTests.StepDefinitions;

[Binding]
public sealed class ProductSteps
{
    private readonly ProductApi _api = new(AcceptanceHost.Factory.CreateClient());
    private ProductDto? _product;
    private HttpResponseMessage? _response;
    private List<ProductDto> _listed = [];

    [Given("a product {string} with {int} units in stock")]
    public async Task GivenAProduct(string name, int stock)
        => _product = await _api.CreateOkAsync(ProductApi.NewProduct(name: name, stock: stock));

    [When("I remove {int} units from stock")]
    public async Task WhenIRemoveUnits(int quantity) => _response = await _api.DecrementAsync(_product!.Id, quantity);

    [When("I add {int} units to stock")]
    public async Task WhenIAddUnits(int quantity) => _response = await _api.AddAsync(_product!.Id, quantity);

    [When("I search for {string}")]
    public async Task WhenISearchFor(string term) => _listed = await _api.ListAsync($"/search?name={Uri.EscapeDataString(term)}");

    [When("I list products with stock between {int} and {int}")]
    public async Task WhenIListByStock(int min, int max) => _listed = await _api.ListAsync($"/stock-level?min={min}&max={max}");

    [Then("the request succeeds")]
    public void ThenTheRequestSucceeds() => Assert.Equal(HttpStatusCode.OK, _response!.StatusCode);

    [Then("the request is rejected as a conflict")]
    public void ThenConflict() => Assert.Equal(HttpStatusCode.Conflict, _response!.StatusCode);

    [Then("the request is rejected as invalid")]
    public void ThenInvalid() => Assert.Equal(HttpStatusCode.BadRequest, _response!.StatusCode);

    [Then("the product has {int} units in stock")]
    public async Task ThenTheProductHasStock(int expected)
    {
        var current = await _api.Http.GetFromJsonAsync<ProductDto>($"/api/products/{_product!.Id}");
        Assert.Equal(expected, current!.Stock);
    }

    [Then("I see only {string}")]
    public void ThenISeeOnly(string name) => Assert.Equal(name, Assert.Single(_listed).Name);

    [Then("every listed product has between {int} and {int} units in stock")]
    public void ThenEveryListedProductIsInRange(int min, int max) => Assert.All(_listed, p => Assert.InRange(p.Stock, min, max));

    [Then("the list includes {string}")]
    public void ThenTheListIncludes(string name) => Assert.Contains(_listed, p => p.Name == name);
}
