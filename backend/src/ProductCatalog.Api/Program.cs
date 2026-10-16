using ProductCatalog.Api;
using ProductCatalog.Api.Endpoints;
using ProductCatalog.Api.Infrastructure;
using ProductCatalog.Application;
using ProductCatalog.Infrastructure;
using ProductCatalog.Infrastructure.Persistence;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(kestrel => kestrel.AddServerHeader = false);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApiServices(builder.Configuration);

var app = builder.Build();

// First, so HTTPS redirection, rate limiting and logging all see the real client address and scheme.
app.UseForwardedHeaders();
// Outside the exception handler, so the log shows the status the client really got, 500s included.
app.UseHttpLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseSecurityHeaders();
app.UseCors(ApiServiceCollectionExtensions.FrontendCorsPolicy);
app.UseRateLimiter();
app.UseRequestTimeouts();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("ZEISS Product Catalog API"));
}

app.MapApiHealthChecks();
app.MapProductEndpoints();
app.MapCategoryEndpoints();

var databaseOptions = app.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
if (databaseOptions.ApplyMigrationsOnStartup)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitialiseAsync(databaseOptions.SeedSampleData);
}

await app.RunAsync();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
