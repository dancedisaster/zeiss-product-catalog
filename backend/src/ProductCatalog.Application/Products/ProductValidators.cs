using FluentValidation;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Application.Products;

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        // One message per field: a missing value is reported as required, not also as badly formatted.
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Name).Required().MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(x => x.Sku).ValidSku();
        RuleFor(x => x.Price).Required().ValidPrice();
        RuleFor(x => x.Stock).Required().GreaterThanOrEqualTo(0);
        RuleFor(x => x.CategoryId).Required().GreaterThan(0);
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleLevelCascadeMode = CascadeMode.Stop;

        RuleFor(x => x.Name).Required().MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(x => x.Sku).ValidSku();
        RuleFor(x => x.Price).Required().ValidPrice();
        RuleFor(x => x.CategoryId).Required().GreaterThan(0);
        // Optional, so clients written to the original contract keep working (see ProductService.UpdateAsync).
        RuleFor(x => x.Version).NotEqual(Guid.Empty).WithMessage("'Version' must be the version returned with the product.");
    }
}

internal static class ProductRuleExtensions
{
    public const decimal MaxPrice = 1_000_000m;
    public const string RequiredMessage = "'{PropertyName}' is required.";

    public static IRuleBuilderOptions<T, string> Required<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().WithMessage(RequiredMessage);

    public static IRuleBuilderOptions<T, TValue?> Required<T, TValue>(this IRuleBuilder<T, TValue?> rule)
        where TValue : struct
        => rule.NotNull().WithMessage(RequiredMessage);

    public static IRuleBuilderOptions<T, string> ValidSku<T>(this IRuleBuilder<T, string> rule)
        => rule.Required()
            .MaximumLength(Product.SkuMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("SKU may only contain letters, digits and dashes.");

    public static IRuleBuilderOptions<T, decimal?> ValidPrice<T>(this IRuleBuilder<T, decimal?> rule)
        => rule.GreaterThan(0)
            .LessThanOrEqualTo(MaxPrice)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);
}
