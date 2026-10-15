namespace ProductCatalog.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Persists pending changes atomically.
    /// Throws <see cref="Common.ConcurrencyConflictException"/> or
    /// <see cref="Common.UniqueConstraintViolationException"/> for the expected database conflicts.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
