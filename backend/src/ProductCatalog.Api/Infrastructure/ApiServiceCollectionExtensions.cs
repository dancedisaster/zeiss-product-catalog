using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using ProductCatalog.Api.Endpoints;

namespace ProductCatalog.Api.Infrastructure;

internal static class ApiServiceCollectionExtensions
{
    public const string FrontendCorsPolicy = "frontend";

    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        // RFC 9457 problem details for every error, with a trace id to correlate with logs.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // One log entry per request: method, path, query, status, duration and the traceId error responses carry.
        // No headers or bodies, which can hold credentials and personal data.
        services.AddHttpLogging(options =>
        {
            options.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath | HttpLoggingFields.RequestQuery
                | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });
        services.AddHttpLoggingInterceptor<TraceIdLoggingInterceptor>();

        // A request that runs too long is cancelled (the token reaches EF Core, which cancels the SQL command)
        // and answered with 504, so slow work cannot pile up. ASP.NET Core skips this while a debugger is attached.
        var timeout = configuration.GetValue("RequestTimeouts:Default", TimeSpan.FromSeconds(30));
        services.AddRequestTimeouts(options => options.DefaultPolicy = new RequestTimeoutPolicy { Timeout = timeout });

        services.AddOpenApi();

        // Only the configured frontend origin(s) may call the API from a browser.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options => options.AddPolicy(FrontendCorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .WithMethods("GET", "POST", "PUT", "DELETE")
            .WithHeaders("Content-Type")
            // Browsers hide non-standard response headers from cross-origin scripts unless they are exposed.
            .WithExposedHeaders(ProductEndpoints.TotalCountHeader)));

        // Behind a reverse proxy every request arrives from the proxy's address, which would put all users
        // in one rate-limit partition. Take the client address from X-Forwarded-For, but only when the request
        // comes from a configured proxy: from anyone else the header could be forged to dodge the limit.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });

        // Basic abuse protection: a fixed window per client IP.
        var permitLimit = configuration.GetValue("RateLimiting:PermitPerMinute", 300);
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    public static void MapApiHealthChecks(this WebApplication app)
    {
        // Liveness: the process answers. Readiness: dependencies (database) are reachable.
        // Probes arrive every few seconds, so they are neither rate limited nor logged.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .DisableRateLimiting()
            .WithHttpLogging(HttpLoggingFields.None);
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
            .DisableRateLimiting()
            .WithHttpLogging(HttpLoggingFields.None);
    }
}
