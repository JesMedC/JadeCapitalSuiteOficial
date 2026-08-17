using JadeCapital.Shared.Kernel.Storage;

namespace JadeCapital.Trading.Infrastructure.Storage;

/// <summary>
/// Default <see cref="IVirusScanner"/> implementation for Wave 4d.
///
/// Wave 4 ships with this no-op that ALWAYS returns
/// <see cref="VirusScanResult.Clean"/>. Wave 6 swaps in a real ClamAV
/// impl (REST client to <c>clamd</c>, signature DB updates, etc.) by
/// replacing this registration in <c>TradingModuleRegistration</c>.
/// The rest of the system depends only on the <see cref="IVirusScanner"/>
/// interface, so the swap is transparent.
///
/// Why no-op:
/// <list type="bullet">
///   <item>The ConfirmAttachmentUploadedHandler still calls the scanner
///   + records <c>scan_result = Clean</c> + <c>virus_scanned_at = now</c>
///   on the attachment row. Wave 6 can backfill these audit fields
///   retroactively without breaking the wire contract.</item>
///   <item>Returning a fixed result keeps tests deterministic and avoids
///   requiring a ClamAV container in the integration harness.</item>
/// </list>
/// </summary>
public sealed class VirusScannerNoOp : IVirusScanner
{
    /// <inheritdoc />
    public Task<VirusScanResult> ScanAsync(Stream content, string mimeType, CancellationToken ct = default)
    {
        // The Wave 4d default trusts the upload. We DO NOT consume the
        // stream — the handler has already buffered the bytes elsewhere
        // (or the storage verify step after Confirm pulls them from
        // MinIO). Reading here would lock the caller's stream position.
        //
        // Return Clean so the handler records scan_result = Clean +
        // virus_scanned_at = now and the upload proceeds.
        return Task.FromResult(VirusScanResult.Clean);
    }
}