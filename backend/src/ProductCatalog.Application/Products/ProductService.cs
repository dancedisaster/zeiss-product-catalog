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
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private static readonly ProductOrder ByName = new(ProductSortColumn.Name);
    private static readonly ProductOrder ByStock = new(ProductSortColumn.Stock);

    private static readonly Dictionary<string, ProductSortColumn> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ProductSortColumn.Name,
        ["categoryName"] = ProductSortColumn.CategoryName,
        ["price"] = ProductSortColumn.Price,
        ["stock"] = ProductSortColumn.Stock,
    };

    public Task<Result<ProductPage>> ListAsync(ListOptions options, CancellationToken cancellationToken)
        => QueryAsync(new ProductFilter(), ByName, options, new Dictionary<string, string[]>(), cancellationToken);

    public async Task<Result<ProductDto>> GetAsync(int id, CancellationToken cancellationToken)
    {
        var product = await products.GetByIdAsync(id, cancellationToken);
        return product is null ? ProductNotFound(id) : product.ToDto();
    }

    public async Task<Result<ProductPage>> SearchAsync(
        string? name, int? min, int? max, ListOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("name", "A search term is required.");
        }

        if (name.Length > SearchTermMaxLength)
        {
            return Error.Validation("name", $"The search term must be at most {SearchTermMaxLength} characters.");
        }

        // The stock range is optional here; it lets a client combine both filters and still page correctly.
        var errors = StockRangeErrors(min, max);
        return await QueryAsync(new ProductFilter(name.Trim(), min, max), ByName, options, errors, cancellationToken);
    }

    public Task<Result<ProductPage>> ListByStockLevelAsync(int? min, int? max, ListOptions options, CancellationToken cancellationToken)
        => QueryAsync(new ProductFilter(MinStock: min ?? 0, MaxStock: max), ByStock, options, StockRangeErrors(min, max), cancellationToken);

    public Task<ProductSummary> GetSummaryAsync(CancellationToken cancellationToken)
        => products.GetSummaryAsync(cancellationToken);

    public async Task<Result<ProductDto>> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        // From here on the validator guarantees the numeric fields are present.
        var category = await categories.GetByIdAsync(request.CategoryId!.Value, cancellationToken);
        if (category is null)
        {
            return CategoryNotFound(request.CategoryId.Value);
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
            request.Price!.Value,
            request.Stock!.Value,
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

        // The client sends the version it loaded. A different one means someone else saved the
        // details since then, and applying this update would silently overwrite their change.
        if (product.Version != request.Version)
        {
            return ConcurrentUpdate(id);
        }

        var category = await categories.GetByIdAsync(request.CategoryId!.Value, cancellationToken);
        if (category is null)
        {
            return CategoryNotFound(request.CategoryId.Value);
        }

        if (await products.SkuExistsAsync(request.Sku, excludingProductId: id, cancellationToken))
        {
            return DuplicateSku(request.Sku);
        }

        product.UpdateDetails(request.Name, request.Description, request.Sku, request.Price!.Value, category, clock.GetUtcNow());

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
            // Someone saved between our read and our write (the version check above runs too early to see it).
            return ConcurrentUpdate(id);
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

    /// <summary>Validates paging and sorting next to the endpoint's own errors, so all problems are reported at once.</summary>
    private async Task<Result<ProductPage>> QueryAsync(
        ProductFilter filter,
        ProductOrder defaultOrder,
        ListOptions options,
        Dictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var order = ReadOrder(options, defaultOrder, errors);
        var page = ReadPage(options, errors);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        var result = await products.QueryAsync(filter, order, page, cancellationToken);
        return new ProductPage(result.Items.ToDtos(), result.TotalCount);
    }

    private static ProductOrder ReadOrder(ListOptions options, ProductOrder defaultOrder, Dictionary<string, string[]> errors)
    {
        if (options.SortBy is null)
        {
            if (options.SortDir is not null)
            {
                errors["sortDir"] = ["'sortDir' can only be used together with 'sortBy'."];
            }

            return defaultOrder;
        }

        if (!SortColumns.TryGetValue(options.SortBy, out var column))
        {
            errors["sortBy"] = [$"'sortBy' must be one of: {string.Join(", ", SortColumns.Keys)}."];
            return defaultOrder;
        }

        var descending = options.SortDir?.ToUpperInvariant() switch
        {
            null or "ASC" => false,
            "DESC" => true,
            _ => (bool?)null,
        };
        if (descending is null)
        {
            errors["sortDir"] = ["'sortDir' must be 'asc' or 'desc'."];
        }

        return new ProductOrder(column, descending ?? false);
    }

    /// <summary>Paging is opt-in: without page and pageSize every match is returned, as the original contract requires.</summary>
    private static PageRequest? ReadPage(ListOptions options, Dictionary<string, string[]> errors)
    {
        if (options.Page is null && options.PageSize is null)
        {
            return null;
        }

        var page = new PageRequest(options.Page ?? 1, options.PageSize ?? DefaultPageSize);
        if (page.Page < 1)
        {
            errors["page"] = ["'page' must be 1 or greater."];
        }

        if (page.PageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"'pageSize' must be between 1 and {MaxPageSize}."];
        }

        return page;
    }

    private static Dictionary<string, string[]> StockRangeErrors(int? min, int? max)
    {
        var errors = new Dictionary<string, string[]>();
        if (min < 0)
        {
            errors["min"] = ["'min' must be greater than or equal to 0."];
        }

        if (max < (min ?? 0))
        {
            errors["max"] = ["'max' must be greater than or equal to 'min'."];
        }

        return errors;
    }

    private static Error InvalidQuantity() => Error.Validation("quantity", "Quantity must be greater than zero.");

    private static Error ProductNotFound(int id) => Error.NotFound("product.not_found", $"Product {id} was not found.");

    private static Error CategoryNotFound(int categoryId) => Error.Validation("categoryId", $"Category {categoryId} does not exist.");

    private static Error ConcurrentUpdate(int id)
        => Error.Conflict("product.concurrent_update", $"Product {id} was changed by someone else. Reload it and try again.");

    private static Error DuplicateSku(string sku)
        => Error.Conflict("product.duplicate_sku", $"A product with SKU '{sku.Trim().ToUpperInvariant()}' already exists.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Created product {ProductId} ({Sku})")]
    private partial void LogProductCreated(int productId, string sku);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted product {ProductId}")]
    private partial void LogProductDeleted(int productId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock of product {ProductId} changed by {Delta}, now {Stock}")]
    private partial void LogStockChanged(int productId, int delta, int stock);
}
