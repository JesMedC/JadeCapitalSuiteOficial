namespace JadeCapital.Trading.Domain.Imports;

/// <summary>
/// Lifecycle status of an <see cref="ImportJob"/>. Mirrors the CHECK
/// constraint on <c>trading.import_jobs.status</c> (status BETWEEN 0 AND 4)
/// declared in migration 0019_import_jobs.sql — do NOT renumber without
/// a DB migration.
/// </summary>
public enum ImportJobStatus : byte
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4,
}