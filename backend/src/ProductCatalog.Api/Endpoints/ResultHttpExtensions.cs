using Microsoft.AspNetCore.Http.HttpResults;
using ProductCatalog.Application.Common;

namespace ProductCatalog.Api.Endpoints;

/// <summary>The single place where application errors become HTTP status codes.</summary>
internal static class ResultHttpExtensions
{
    public static ProblemHttpResult ToProblem(this Error error) => error.Type switch
    {
        ErrorType.Validation => TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed",
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code, ["errors"] = error.ValidationErrors }),
        ErrorType.NotFound => Problem(StatusCodes.Status404NotFound, "Resource not found", error),
        ErrorType.Conflict => Problem(StatusCodes.Status409Conflict, "Conflict", error),
        _ => throw new ArgumentOutOfRangeException(nameof(error), error.Type, "Unknown error type."),
    };

    private static ProblemHttpResult Problem(int status, string title, Error error)
        => TypedResults.Problem(
            statusCode: status,
            title: title,
            detail: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
