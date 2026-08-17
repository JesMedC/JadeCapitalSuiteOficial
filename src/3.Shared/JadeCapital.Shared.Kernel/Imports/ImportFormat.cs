namespace JadeCapital.Shared.Kernel.Imports;

/// <summary>
/// Wire-format identifier for an import parser. Mirrors the CHECK constraint
/// on <c>trading.import_jobs.format</c> (format IN (0, 1, 2, 255)) declared
/// in migration 0019_import_jobs.sql — do NOT renumber without a DB migration.
/// </summary>
public enum ImportFormat : byte
{
    Csv = 0,
    Mt4 = 1,
    Mt5 = 2,
    Unknown = 255,
}