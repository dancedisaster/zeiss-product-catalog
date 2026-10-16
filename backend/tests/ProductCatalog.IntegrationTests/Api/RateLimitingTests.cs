using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.IntegrationTests.Infrastructure;

namespace ProductCatalog.IntegrationTests.Api;

/// <summary>
/// Behind nginx every request reaches the API from the proxy's address. The rate limit must still apply
/// per client, and a client that calls the API directly must not dodge it by forging X-Forwarded-For.
/// </summary>
public class RateLimitingTests
{
    private const int PermitPerMinute = 2;
    private static readonly IPAddress TrustedProxy = IPAddress.Parse("172.28.10.10");

    [Fact]
    public async Task Behind_the_trusted_proxy_each_client_gets_its_own_limit()
    {
        await using var api = await StartApiAsync(requestsComeFrom: TrustedProxy);
        var http = api.CreateClient();

        for (var i = 0; i < PermitPerMinute; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await GetAsync(http, forwardedFor: "203.0.113.1"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await GetAsync(http, forwardedFor: "203.0.113.1"));
        Assert.Equal(HttpStatusCode.OK, await GetAsync(http, forwardedFor: "203.0.113.2"));
    }

    [Fact]
    public async Task Forwarded_for_sent_by_anyone_but_the_proxy_is_ignored()
    {
        await using var api = await StartApiAsync(requestsComeFrom: IPAddress.Parse("198.51.100.7"));
        var http = api.CreateClient();

        // A new forged address on every request: if the header were trusted, each would get a fresh limit.
        for (var i = 0; i < PermitPerMinute; i++)
        {
            Assert.Equal(HttpStatusCode.OK, await GetAsync(http, forwardedFor: $"203.0.113.{i + 1}"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await GetAsync(http, forwardedFor: "203.0.113.99"));
    }

    private static async Task<CatalogApiFactory> StartApiAsync(IPAddress requestsComeFrom)
    {
        var connectionString = await SqlServerContainer.CreateDatabaseConnectionStringAsync();
        return new CatalogApiFactory(connectionString, seedSampleData: false, host => host
            .UseSetting("RateLimiting:PermitPerMinute", PermitPerMinute.ToString(CultureInfo.InvariantCulture))
            .UseSetting("ForwardedHeaders:KnownProxies:0", TrustedProxy.ToString())
            .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new RemoteAddressStartupFilter(requestsComeFrom))));
    }

    private static async Task<HttpStatusCode> GetAsync(HttpClient http, string forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        using var response = await http.SendAsync(request);
        return response.StatusCode;
    }

    /// <summary>The in-memory test server has no TCP connection; this sets the address requests appear to come from.</summary>
    private sealed class RemoteAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
