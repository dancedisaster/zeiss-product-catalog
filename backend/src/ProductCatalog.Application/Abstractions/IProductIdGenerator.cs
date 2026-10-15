namespace ProductCatalog.Application.Abstractions;

/// <summary>
/// Produces unique 6-digit product ids. Implementations must stay unique when several
/// API instances run at the same time, so the source of truth cannot live in process memory.
/// </summary>
public interface IProductIdGenerator
{
    Task<int> NextIdAsync(CancellationToken cancellationToken);
}
