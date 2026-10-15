namespace ProductCatalog.Domain.Common;

/// <summary>
/// Raised when an operation would break a business invariant of the domain model.
/// The domain is the last line of defence: the application layer validates input first,
/// so reaching this exception usually means a rule (e.g. "not enough stock") was violated.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
