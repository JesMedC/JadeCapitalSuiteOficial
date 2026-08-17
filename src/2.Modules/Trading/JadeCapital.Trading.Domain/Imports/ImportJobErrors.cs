using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Domain.Imports;

/// <summary>
/// Error catalog for the bounded context of Import Jobs (slice 5a.1,
/// Wave 5). Codes carry the <c>import_job.</c> prefix so the import
/// endpoints can distinguish them from other domain errors (Trade,
/// PreTradeChecklist, Scanner, etc.) when mapping to HTTP status codes.
///
/// Invariant mapping (mirrors existing scanner/checklist errors):
/// <list type="bullet">
///   <item>validation.* → 400 / 422 (semantic validation failure)</item>
///   <item>conflict.* → 409 (idempotency / state collision)</item>
///   <item>notfound.* → 404 (resource not in user's namespace)</item>
/// </list>
/// </summary>
public static class ImportJobErrors
{
    public static class Errors
    {
        public static readonly Error UserIdRequired =
            Error.Validation("import_job.user_id_required", "Import job user id is required.");

        public static readonly Error AccountIdRequired =
            Error.Validation("import_job.account_id_required", "Import job account id is required.");

        public static readonly Error FileNameRequired =
            Error.Validation("import_job.file_name_required", "Import job file name is required.");

        public static readonly Error FileSizeInvalid =
            Error.Validation("import_job.file_size_invalid", "Import job file size must be greater than zero.");

        public static readonly Error FileTooLarge =
            Error.Validation("import_job.file_too_large", "Import job file size cannot exceed 10 MiB.");

        public static readonly Error Sha256Invalid =
            Error.Validation("import_job.sha256_invalid", "Import job SHA-256 must be exactly 64 hex characters.");

        public static readonly Error RowsNonNegative =
            Error.Validation("import_job.rows_non_negative", "Import job row counters must be non-negative.");

        public static readonly Error InvalidStatusTransition =
            Error.Validation("import_job.invalid_status_transition",
                "Import job cannot transition to the requested status from its current status.");

        public static readonly Error ErrorMessageTooLong =
            Error.Validation("import_job.error_message_too_long",
                "Import job error message must be 2000 characters or less.");

        public static readonly Error NotFound =
            Error.NotFound("import_job.not_found", "Import job not found.");
    }
}