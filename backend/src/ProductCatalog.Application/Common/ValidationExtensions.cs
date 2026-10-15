using System.Text.Json;
using FluentValidation.Results;

namespace ProductCatalog.Application.Common;

internal static class ValidationExtensions
{
    /// <summary>Groups FluentValidation failures by camelCase field name, matching the JSON contract.</summary>
    public static Error ToError(this ValidationResult result)
        => Error.Validation(result.Errors
            .GroupBy(failure => JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).ToArray()));
}
