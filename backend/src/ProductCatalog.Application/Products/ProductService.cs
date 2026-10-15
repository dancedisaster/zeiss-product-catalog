using FluentValidation;
using Microsoft.Extensions.Logging;
using ProductCatalog.Application.Abstractions;
using ProductCatalog.Application.Common;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Application.Products;

internal sealed partial class ProductService(
    IProductRepository products,
    ICategoryRepository categories,
    IProductIdGenerator idGenerator,
    IUnitOfWork unitOfWork,
    IValidator<CreateProductRequest> createValidator,
    IValidator<UpdateProductRequest> updateValidator,
    TimeProvider clock,
    ILogger<ProductService> logger) : IProductService
{
    public const int SearchTermMaxLength = Product.NameMaxLength;

    public async Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken cancellationToken)
        => (await products.ListAsync(cancellationToken)).ToDtos();

    public async Task<Result<ProductDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var product = await products.GetByIdAsync(id, cancellationToken);
        return product is null ? ProductNotFound(id) : product.ToDto();
    }

    public async Task<Result<IReadOnlyList<ProductDto>>> SearchAsync(string? name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("name", "A search term is required.");
        }

        if (name.Length > SearchTermMaxLength)
        {
            return Error.Validation("name", $"The search term must be at most {SearchTermMaxLength} characters.");
        }

        return ToResult(await products.SearchByNameAsync(name.Trim(), cancellationToken));
    }

    public async Task<Result<IReadOnlyList<ProductDto>>> ListByStockLevelAsync(int? min, int? max, CancellationToken cancellationToken)
    {
        var lower = min ?? 0;
        var upper = max ?? int.MaxValue;

        var errors = new Dictionary<string, string[]>();
        if (lower < 0)
        {
            errors["min"] = ["'min' must be greater than or equal to 0."];
        }

        if (upper < lower)
        {
            errors["max"] = ["'max' must be greater than or equal to 'min'."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        return ToResult(await products.ListByStockRangeAsync(lower, upper, cancellationToken));
    }

    public async Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var category = await categories.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category is null)
        {
            return CategoryNotFound(request.CategoryId);
        }

        if (await products.SkuExistsAsync(request.Sku, excludingProductId: null, cancellationToken))
        {
            return DuplicateSku(request.Sku);
        }

        var id = await idGenerator.NextIdAsync(cancellationToken);
        var product = Product.Create(
            id,
            request.Name,
            request.Description,
            request.Sku,
            request.Price,
            request.Stock,
            category,
            clock.GetUtcNow());

        products.Add(product);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Another request inserted the same SKU between our check and our insert.
            return DuplicateSku(request.Sku);
        }

        LogProductCreated(product.Id, product.Sku);
        return product.ToDto();
    }

    public async Task<Result<ProductDto>> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        var product = await products.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return ProductNotFound(id);
        }

        var category = await categories.GetByIdAsync(request.CategoryId, cancellationToken);
        if (category is null)
        {
            return CategoryNotFound(request.CategoryId);
        }

        if (await products.SkuExistsAsync(request.Sku, excludingProductId: id, cancellationToken))
        {
            return DuplicateSku(request.Sku);
        }

        product.UpdateDetails(request.Name, request.Description, request.Sku, request.Price, category, clock.GetUtcNow());

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return DuplicateSku(request.Sku);
        }
        catch (ConcurrencyConflictException)
        {
            return Error.Conflict(
                "product.concurrent_update",
                $"Product {id} was changed by another request. Reload it and try again.");
        }

        return product.ToDto();
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var product = await products.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return ProductNotFound(id);
        }

        products.Remove(product);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            // Deleted (or changed) concurrently: treat a vanished product as not found.
            return ProductNotFound(id);
        }

        LogProductDeleted(id);
        return Result.Success();
    }

    public async Task<Result<ProductDto>> AddStockAsync(int id, int quantity, CancellationToken cancellationToken)
        => quantity > 0 ? await ChangeStockAsync(id, quantity, cancellationToken) : InvalidQuantity();

    public async Task<Result<ProductDto>> DecrementStockAsync(int id, int quantity, CancellationToken cancellationToken)
        => quantity > 0 ? await ChangeStockAsync(id, -quantity, cancellationToken) : InvalidQuantity();

    private async Task<Result<ProductDto>> ChangeStockAsync(int id, int delta, CancellationToken cancellationToken)
    {
        var changed = await products.TryChangeStockAsync(id, delta, clock.GetUtcNow(), cancellationToken);
        var product = await products.GetByIdAsync(id, cancellationToken);

        if (product is null)
        {
            return ProductNotFound(id);
        }

        if (changed)
        {
            LogStockChanged(id, delta, product.Stock);
            return product.ToDto();
        }

        return delta < 0
            ? Error.Conflict(
                "product.insufficient_stock",
                $"Product {id} has {product.Stock} unit(s) in stock; cannot remove {-delta}.")
            : Error.Validation("quantity", "The resulting stock would exceed the maximum allowed value.");
    }

    private static Result<IReadOnlyList<ProductDto>> ToResult(IReadOnlyList<Product> items)
        => Result<IReadOnlyList<ProductDto>>.Success(items.ToDtos());

    private static Error InvalidQuantity() => Error.Validation("quantity", "Quantity must be greater than zero.");

    private static Error ProductNotFound(int id) => Error.NotFound("product.not_found", $"Product {id} was not found.");

    private static Error CategoryNotFound(int categoryId) => Error.Validation("categoryId", $"Category {categoryId} does not exist.");

    private static Error DuplicateSku(string sku)
        => Error.Conflict("product.duplicate_sku", $"A product with SKU '{sku.Trim().ToUpperInvariant()}' already exists.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Created product {ProductId} ({Sku})")]
    private partial void LogProductCreated(int productId, string sku);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted product {ProductId}")]
    private partial void LogProductDeleted(int productId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock of product {ProductId} changed by {Delta}, now {Stock}")]
    private partial void LogStockChanged(int productId, int delta, int stock);
}
