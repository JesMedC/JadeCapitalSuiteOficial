namespace JadeCapital.Trading.Application._Common;

using JadeCapital.Shared.Kernel.Results;

/// <summary>
/// Application-level errors for attachment quota enforcement + usage
/// reporting (slice 4d, Wave 4).
///
/// Codes:
/// <list type="bullet">
///   <item><c>attachment.quota_exceeded</c> — pre-upload gate tripped
///   (total bytes or count). Maps to HTTP 413 (Payload Too Large).</item>
///   <item><c>attachment.thumbnail_not_supported</c> — request was for a
///   non-image MIME type. Maps to HTTP 415 (Unsupported Media Type).</item>
///   <item><c>attachment.scanner_unavailable</c> — virus scanner threw
///   <c>ScannerUnavailableException</c>. Maps to HTTP 503.</item>
/// </list>
/// </summary>
public static class AttachmentsErrors
{
    public static readonly Error QuotaExceeded =
        Error.Failure("attachment.quota_exceeded",
            "Attachment quota exceeded. Delete old attachments or upgrade tier.");

    public static readonly Error ThumbnailNotSupported =
        Error.Failure("attachment.thumbnail_not_supported",
            "Thumbnails are only available for image attachments.");

    public static readonly Error ScannerUnavailable =
        Error.Failure("attachment.scanner_unavailable",
            "Virus scanner is currently unavailable. Try again in a moment.");
}