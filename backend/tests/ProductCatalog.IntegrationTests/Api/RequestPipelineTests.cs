using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Application.Categories;
using ProductCatalog.Infrastructure.Persistence;
using ProductCatalog.IntegrationTests.Infrastructure;

namespace ProductCatalog.IntegrationTests.Api;

/// <summary>Request timeouts and request logging, observed from the outside.</summary>
public class RequestPipelineTests
{
    private const string RequestLog = "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware";

    [Fact]
    public async Task A_request_that_runs_too_long_is_cancelled_and_answered_with_504_problem_details()
    {
        var connectionString = await SqlServerContainer.CreateDatabaseConnectionStringAsync();
        await using var api = new CatalogApiFactory(connectionString, seedSampleData: false, host => host
            .UseSetting("RequestTimeouts:Default", "00:00:00.200")
            .ConfigureTestServices(services => services.AddScoped<ICategoryService, NeverFinishingCategoryService>()));

        var response = await api.CreateClient().GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task A_sql_command_cut_short_by_the_timeout_is_answered_with_504_too()
    {
        var connectionString = await SqlServerContainer.CreateDatabaseConnectionStringAsync();
        await using var api = new CatalogApiFactory(connectionString, seedSampleData: false, host => host
            .UseSetting("RequestTimeouts:Default", "00:00:00.500")
            .ConfigureTestServices(services => services.AddScoped<ICategoryService, SlowQueryCategoryService>()));

        var response = await api.CreateClient().GetAsync("/api/categories");

        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Writes_are_not_cut_short_by_the_timeout()
    {
        var connectionString = await SqlServerContainer.CreateDatabaseConnectionStringAsync();
        await using var api = new CatalogApiFactory(connectionString, seedSampleData: false, host => host
            .UseSetting("RequestTimeouts:Default", "00:00:00.200")
            .ConfigureTestServices(services => services.AddScoped<IUnitOfWork, SlowUnitOfWork>()));

        var response = await new ProductApi(api.CreateClient()).CreateAsync(ProductApi.NewProduct());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Each_request_is_logged_with_its_outcome_but_never_with_its_headers()
    {
        var logs = new CapturedLogs();
        await using var api = await StartAsync(logs);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/products/search?name=lens");
        request.Headers.Add("Authorization", "Bearer top-secret-token");

        Assert.Equal(HttpStatusCode.OK, (await api.CreateClient().SendAsync(request)).StatusCode);

        var entry = Assert.Single(await logs.WaitForAsync(RequestLog, "/api/products/search"));
        Assert.Contains("Method: GET", entry, StringComparison.Ordinal);
        Assert.Contains("QueryString: ?name=lens", entry, StringComparison.Ordinal);
        Assert.Contains("StatusCode: 200", entry, StringComparison.Ordinal);
        Assert.Contains("Duration:", entry, StringComparison.Ordinal);
        Assert.DoesNotContain("top-secret-token", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_trace_id_of_an_error_response_leads_to_its_request_log_entry()
    {
        var logs = new CapturedLogs();
        await using var api = await StartAsync(logs);

        var response = await api.CreateClient().GetAsync("/api/products/999999");
        var problem = await response.Content.ReadFromJsonAsync<TracedProblem>();

        var entry = Assert.Single(await logs.WaitForAsync(RequestLog, "/api/products/999999"));
        Assert.Contains("StatusCode: 404", entry, StringComparison.Ordinal);
        Assert.Contains($"TraceId: {problem!.TraceId}", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_probes_are_not_logged()
    {
        var logs = new CapturedLogs();
        await using var api = await StartAsync(logs);
        var http = api.CreateClient();

        await http.GetAsync("/health/live");
        await http.GetAsync("/health/ready");
        await http.GetAsync("/api/categories");
        Assert.NotEmpty(await logs.WaitForAsync(RequestLog, "/api/categories")); // the log has caught up

        Assert.DoesNotContain(logs.Messages(RequestLog), m => m.Contains("/health", StringComparison.Ordinal));
    }

    private static async Task<CatalogApiFactory> StartAsync(CapturedLogs logs)
    {
        var connectionString = await SqlServerContainer.CreateDatabaseConnectionStringAsync();
        return new CatalogApiFactory(connectionString, configure: host => host.ConfigureLogging(logging => logging.AddProvider(logs)));
    }

    private sealed record TracedProblem(string TraceId);

    /// <summary>Only a cancelled token ends it, so the request can only finish through the timeout.</summary>
    private sealed class NeverFinishingCategoryService : ICategoryService
    {
        public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }

    /// <summary>A real SQL command that outlasts the timeout, so the cancellation has to go through SqlClient.</summary>
    private sealed class SlowQueryCategoryService(CatalogDbContext db) : ICategoryService
    {
        public async Task<IReadOnlyList<CategoryDto>> ListAsync(CancellationToken cancellationToken)
        {
            await db.Database.ExecuteSqlRawAsync("WAITFOR DELAY '00:00:30'", cancellationToken);
            return [];
        }
    }

    /// <summary>Saves, but only after the request timeout has passed.</summary>
    private sealed class SlowUnitOfWork(CatalogDbContext db) : IUnitOfWork
    {
        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
