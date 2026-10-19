using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ProductCatalog.IntegrationTests.Infrastructure;

/// <summary>Hosts the real API in memory, pointed at a test database. Two factories on the same
/// connection string behave like two instances of the service behind a load balancer.</summary>
/// <param name="configure">Optional per-test overrides, applied after the defaults.</param>
public sealed class CatalogApiFactory(
    string connectionString,
    bool seedSampleData = true,
    Action<IWebHostBuilder>? configure = null) : WebApplicationFactory<Program>
{
    public string ConnectionString { get; } = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Catalog", ConnectionString);
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Database:SeedSampleData", seedSampleData.ToString());
        builder.UseSetting("RateLimiting:PermitPerMinute", "100000");
        configure?.Invoke(builder);
    }
}

/// <summary>xUnit fixture: one API + database per test class.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public CatalogApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Factory = new CatalogApiFactory(await SqlServerContainer.CreateDatabaseConnectionStringAsync());
        _ = Factory.Server; // start the host now: applies migrations and seeds
    }

    public async Task DisposeAsync() => await Factory.DisposeAsync();
}
