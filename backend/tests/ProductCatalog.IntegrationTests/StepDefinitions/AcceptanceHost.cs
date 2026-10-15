using ProductCatalog.IntegrationTests.Infrastructure;
using Reqnroll;

namespace ProductCatalog.IntegrationTests.StepDefinitions;

/// <summary>One API + database shared by all BDD scenarios of the run.</summary>
[Binding]
public sealed class AcceptanceHost
{
    public static CatalogApiFactory Factory { get; private set; } = null!;

    [BeforeTestRun]
    public static async Task StartAsync()
    {
        Factory = new CatalogApiFactory(await SqlServerContainer.CreateDatabaseConnectionStringAsync(), seedSampleData: false);
        _ = Factory.Server;
    }

    [AfterTestRun]
    public static async Task StopAsync() => await Factory.DisposeAsync();
}
