namespace ProductCatalog.Application.Common;

/// <summary>Another request changed the same row between our read and our write.</summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null)
    : Exception(message, inner);
