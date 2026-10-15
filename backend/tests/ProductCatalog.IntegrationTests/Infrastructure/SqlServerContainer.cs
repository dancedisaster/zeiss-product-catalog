using Testcontainers.MsSql;

namespace ProductCatalog.IntegrationTests.Infrastructure;

/// <summary>
/// One real SQL Server (in Docker) for the whole test run. Each test class gets its own database
/// on it, so tests stay isolated without paying the container start-up cost every time.
/// Requires Docker to be running.
/// </summary>
public static class SqlServerContainer
{
    private static readonly Lazy<Task<MsSqlContainer>> Container = new(async () =>
    {
        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
        await container.StartAsync();
        return container;
    });

    public static async Task<string> CreateDatabaseConnectionStringAsync()
    {
        var container = await Container.Value;
        var databaseName = $"catalog_tests_{Guid.NewGuid():N}";
        return container.GetConnectionString().Replace("Database=master", $"Database={databaseName}", StringComparison.Ordinal);
    }
}
