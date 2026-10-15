using FluentValidation;
using ProductCatalog.Domain.Products;

namespace ProductCatalog.Application.Products;

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(x => x.Sku).ValidSku();
        RuleFor(x => x.Price).ValidPrice();
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CategoryId).GreaterThan(0);
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Product.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Product.DescriptionMaxLength);
        RuleFor(x => x.Sku).ValidSku();
        RuleFor(x => x.Price).ValidPrice();
        RuleFor(x => x.CategoryId).GreaterThan(0);
    }
}

internal static class ProductRuleExtensions
{
    public const decimal MaxPrice = 1_000_000m;

    public static IRuleBuilderOptions<T, string> ValidSku<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty()
            .MaximumLength(Product.SkuMaxLength)
            .Matches("^[A-Za-z0-9-]+$").WithMessage("SKU may only contain letters, digits and dashes.");

    public static IRuleBuilderOptions<T, decimal> ValidPrice<T>(this IRuleBuilder<T, decimal> rule)
        => rule.GreaterThan(0)
            .LessThanOrEqualTo(MaxPrice)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true);
}
