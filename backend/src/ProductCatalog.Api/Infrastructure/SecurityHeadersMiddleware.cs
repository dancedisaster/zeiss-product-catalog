namespace ProductCatalog.Api.Infrastructure;

internal static class SecurityHeadersMiddleware
{
    /// <summary>
    /// The API only returns JSON, so the strictest CSP applies: nothing may load, nothing may frame it.
    /// The interactive API reference (development only) needs scripts, so it is excluded.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            if (!context.Request.Path.StartsWithSegments("/scalar") && !context.Request.Path.StartsWithSegments("/openapi"))
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            await next(context);
        });
}
