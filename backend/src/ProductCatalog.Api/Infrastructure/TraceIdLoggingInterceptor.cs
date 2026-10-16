using System.Diagnostics;
using Microsoft.AspNetCore.HttpLogging;

namespace ProductCatalog.Api.Infrastructure;

/// <summary>
/// Adds to each request log line the same traceId that problem-details responses carry,
/// so an error a user reports can be found in the logs.
/// </summary>
internal sealed class TraceIdLoggingInterceptor : IHttpLoggingInterceptor
{
    public ValueTask OnRequestAsync(HttpLoggingInterceptorContext logContext) => ValueTask.CompletedTask;

    public ValueTask OnResponseAsync(HttpLoggingInterceptorContext logContext)
    {
        if (logContext.LoggingFields != HttpLoggingFields.None)
        {
            logContext.AddParameter("TraceId", Activity.Current?.Id ?? logContext.HttpContext.TraceIdentifier);
        }

        return ValueTask.CompletedTask;
    }
}
