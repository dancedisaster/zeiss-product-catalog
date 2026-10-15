using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using ProductCatalog.Application.Categories;
using ProductCatalog.Application.Products;

namespace ProductCatalog.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateProductRequestValidator>(includeInternalTypes: true);
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddSingleton(TimeProvider.System);
        return services;
    }
}
