namespace JadeCapital.Shared.Kernel.Storage;

/// <summary>
/// Contract for pluggable virus scanning of attachment uploads. Lives in
/// <c>Shared.Kernel.Storage</c> so that the Trading.Application layer can
/// depend on the interface without pulling in the SDK that lives behind
/// the implementation (Wave 6 will swap <c>VirusScannerNoOp</c> for a real
/// ClamAV impl).
///
/// Design notes:
/// <list type="bullet">
///   <item>The scanner consumes a <see cref="Stream"/> (caller passes the
///   upload bytes) plus the declared <c>mimeType</c> for content-aware
///   heuristics. The implementation MUST dispose of the stream when done.</item>
///   <item>Returning a <see cref="VirusScanResult"/> (enum) keeps the contract
///   testable without mocking throwing exceptions for the happy path.</item>
///   <item>The interface throws <c>ScannerUnavailableException</c> for
///   transient infrastructure failures (e.g. ClamAV daemon down). Wave 6+
///   implementations should throw this; the Wave 4d no-op impl never throws.</item>
/// </list>
/// </summary>
public interface IVirusScanner
{
    /// <summary>
    /// Scans <paramref name="content"/> for malware signatures. The
    /// implementation owns the lifetime of <paramref name="content"/> and
    /// may read/dispose it as needed.
    /// </summary>
    /// <param name="content">Readable stream positioned at the start.</param>
    /// <param name="mimeType">Declared MIME type of the upload (image/png, application/pdf, etc.).</param>
    /// <param name="ct">Cancellation token from the HTTP request.</param>
    /// <returns>
    /// <see cref="VirusScanResult.Clean"/> on success, <see cref="VirusScanResult.Infected"/>
    /// on signature match, <see cref="VirusScanResult.Error"/> for non-transient scanner
    /// errors, <see cref="VirusScanResult.Timeout"/> for budget exhaustion.
    /// </returns>
    /// <exception cref="ScannerUnavailableException">
    /// Thrown for transient infrastructure failures (daemon down, network partition).
    /// The ConfirmAttachmentUploadedHandler maps this to HTTP 503.
    /// </exception>
    Task<VirusScanResult> ScanAsync(Stream content, string mimeType, CancellationToken ct = default);
}

/// <summary>
/// Thrown by <see cref="IVirusScanner"/> implementations when the scanner
/// infrastructure is unreachable (ClamAV daemon not running, REST endpoint
/// 503, etc.). Transient — the Confirm endpoint maps to HTTP 503 and the
/// partial MinIO object is deleted.
/// </summary>
public sealed class ScannerUnavailableException : Exception
{
    public ScannerUnavailableException(string message) : base(message) { }
    public ScannerUnavailableException(string message, Exception inner) : base(message, inner) { }
}