namespace ProductCatalog.Api;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Convenient locally. In production migrations run once, as a pipeline step (EF migration bundle),
    /// so several instances starting together never race to migrate the same database.
    /// </summary>
    public bool ApplyMigrationsOnStartup { get; init; }

    public bool SeedSampleData { get; init; }
}
