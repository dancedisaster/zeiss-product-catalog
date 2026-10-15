namespace ProductCatalog.Application.Common;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
}

/// <summary>An expected failure (bad input, missing resource, business rule). Unexpected failures stay exceptions.</summary>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Validation(IReadOnlyDictionary<string, string[]> errors)
        => new("validation_failed", "One or more validation errors occurred.", ErrorType.Validation, errors);

    public static Error Validation(string field, string message)
        => Validation(new Dictionary<string, string[]> { [field] = [message] });
}
