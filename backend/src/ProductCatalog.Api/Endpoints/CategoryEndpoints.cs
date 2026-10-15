using Microsoft.AspNetCore.Http.HttpResults;
using ProductCatalog.Application.Categories;

namespace ProductCatalog.Api.Endpoints;

internal static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/categories", ListAsync)
            .WithTags("Categories")
            .WithSummary("List product categories (used by the product form).");
        return app;
    }

    private static async Task<Ok<IReadOnlyList<CategoryDto>>> ListAsync(ICategoryService service, CancellationToken ct)
        => TypedResults.Ok(await service.ListAsync(ct));
}
