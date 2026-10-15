namespace ProductCatalog.Application.Common;

/// <summary>The database rejected a write because a unique index was violated (e.g. duplicate SKU).</summary>
public sealed class UniqueConstraintViolationException(string message, Exception? inner = null)
    : Exception(message, inner);
