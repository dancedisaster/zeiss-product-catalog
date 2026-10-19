namespace ProductCatalog.Api.Infrastructure;

internal static class RequestCancellationMiddleware
{
    /// <summary>
    /// SqlClient reports a command cancelled through its token as a SqlException ("Operation cancelled by user"), not as
    /// an OperationCanceledException, and EF Core passes it on (inside a DbUpdateException when saving). So whatever fails
    /// while the request is being cancelled is rethrown as the cancellation it is: a timed-out request gets the 504 from
    /// UseRequestTimeouts instead of a 500, and a client that went away is not logged as a server error.
    /// Must run after UseRequestTimeouts, where RequestAborted also fires on the timeout.
    /// </summary>
    public static IApplicationBuilder UseRequestCancellation(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                throw new OperationCanceledException("The request was cancelled.", exception, context.RequestAborted);
            }
        });
}
