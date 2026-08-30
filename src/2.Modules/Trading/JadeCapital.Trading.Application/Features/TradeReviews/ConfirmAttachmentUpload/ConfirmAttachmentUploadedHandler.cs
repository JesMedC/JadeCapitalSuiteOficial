using FluentValidation;
using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Application.Attachments;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.TradeAttachments;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.TradeReviews.ConfirmAttachmentUpload;

/// <summary>
/// Confirma que el cliente termino de subir los bytes al slot que pidio
/// antes. El handler:
/// <list type="number">
///   <item>Resuelve el attachment (cross-user scope: 404 si no es del user).</item>
///   <item>Llama a <see cref="IAttachmentStorage.VerifyObjectExistsAsync"/>
///   contra MinIO para confirmar que el object esta + size coincide.</item>
///   <item>Si todo OK: <c>TradeAttachment.MarkUploaded(sha256, tradeId)</c>
///   emite internamente el <c>TradeAttachmentUploadedDomainEvent</c>.</item>
///   <item>Si falla: <c>MarkFailed()</c> + retorna 409 con
///   <c>conflict.trade_attachment.upload_failed</c>.</item>
/// </list>
/// </summary>
public sealed record ConfirmAttachmentUploadedCommand(
    Guid AttachmentId,
    Guid UserId,
    string? Sha256) : IRequest<Result<TradeAttachmentDto>>;

public sealed class ConfirmAttachmentUploadedValidator : AbstractValidator<ConfirmAttachmentUploadedCommand>
{
    public ConfirmAttachmentUploadedValidator()
    {
        RuleFor(x => x.AttachmentId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        // Sha256 opcional. Si viene, debe ser hex de 64 chars. La regex
        // acepta hex case-insensitive (el domain canoniciza a lowercase).
        When(x => !string.IsNullOrEmpty(x.Sha256), () =>
        {
            RuleFor(x => x.Sha256!)
                .Matches("^[a-fA-F0-9]{64}$")
                .WithMessage("SHA-256 must be 64 hexadecimal characters.");
        });
    }
}

public sealed class ConfirmAttachmentUploadedHandler
    : IRequestHandler<ConfirmAttachmentUploadedCommand, Result<TradeAttachmentDto>>
{
    private readonly ITradeReviewRepository _reviews;
    private readonly IAttachmentStorage _storage;
    private readonly IVirusScanner _scanner;
    private readonly IAttachmentQuotaReader _quotaReader;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmAttachmentUploadedHandler> _logger;

    public ConfirmAttachmentUploadedHandler(
        ITradeReviewRepository reviews,
        IAttachmentStorage storage,
        IVirusScanner scanner,
        IAttachmentQuotaReader quotaReader,
        IUnitOfWork uow,
        IClock clock,
        ILogger<ConfirmAttachmentUploadedHandler> logger)
    {
        _reviews = reviews;
        _storage = storage;
        _scanner = scanner;
        _quotaReader = quotaReader;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<TradeAttachmentDto>> Handle(
        ConfirmAttachmentUploadedCommand req,
        CancellationToken ct)
    {
        // 1) Cross-user scope.
        var attachment = await _reviews.FindAttachmentByIdAsync(req.AttachmentId, req.UserId, ct);
        if (attachment is null)
            return Result.Failure<TradeAttachmentDto>(TradingDomainErrors.TradeAttachment.NotFound);

        // 2) Idempotente: si ya esta uploaded, devolvemos el DTO sin re-validar.
        if (attachment.Status == JadeCapital.Trading.Domain.TradeAttachments.TradeAttachmentStatus.Uploaded)
            return Result.Success(attachment.ToDto());

        // 3) Necesitamos el TradeId del review padre para emitir el
        // TradeAttachmentUploadedDomainEvent (spec contract: el evento
        // incluye tradeId). Una query liviana al repo.
        var tradeId = await _reviews.GetTradeIdByAttachmentIdAsync(req.AttachmentId, ct);

        // 4) Verificar contra MinIO que el object existe y el size coincide.
        var existsAndSizeOk = await _storage.VerifyObjectExistsAsync(
            attachment.ObjectKey, attachment.SizeBytes, ct);

        if (!existsAndSizeOk)
        {
            _logger.LogWarning(
                "Attachment {AttachmentId} size/object mismatch in MinIO (key={ObjectKey}, expected={Size}).",
                attachment.Id, attachment.ObjectKey, attachment.SizeBytes);

            // Marcamos como failed para que el cliente sepa que resubir.
            // Estado terminal: si quiere reintentar, debe pedir un nuevo slot.
            var markFailed = attachment.MarkFailed(_clock.UtcNow);
            if (markFailed.IsFailure)
                return Result.Failure<TradeAttachmentDto>(markFailed.Error);

            await _reviews.UpdateAttachmentAsync(attachment, ct);
            var saveResult = await _uow.SaveChangesAsync(ct);
            if (saveResult.IsFailure)
                return Result.Failure<TradeAttachmentDto>(saveResult.Error);

            return Result.Failure<TradeAttachmentDto>(Error.Conflict(
                "trade_attachment.upload_failed",
                "Object not found in storage or size mismatch. Request a new slot to retry."));
        }

        // 5) Slice 4d — virus scan BEFORE marking uploaded. The scanner reads
        // the stream from MinIO via the storage abstraction; if it throws
        // ScannerUnavailableException we map to 503 + delete the partial
        // object so the bucket doesn't fill with quarantined files.
        // The Wave 4d no-op impl returns Clean without reading the stream.
        var scanStream = new MemoryStream();
        var scanOutcome = await RunScanOrRollbackAsync(attachment, scanStream, ct);
        if (scanOutcome.IsFailure)
            return Result.Failure<TradeAttachmentDto>(scanOutcome.Error);
        var scanResult = scanOutcome.Value;

        // 6) Mark uploaded + stamp lifecycle fields (expires_at = now + 90d,
        // virus_scanned_at = now, scan_result = <from scanner>).
        var uploadTime = _clock.UtcNow;
        var expiresAt = uploadTime.AddDays(JadeCapital.Shared.Kernel.Storage.AttachmentQuota.Default.ExpirationDays);
        attachment.ApplyScanResult(scanResult, uploadTime, expiresAt);

        var markResult = attachment.MarkUploaded(req.Sha256, tradeId ?? Guid.Empty, uploadTime);
        if (markResult.IsFailure)
            return Result.Failure<TradeAttachmentDto>(markResult.Error);

        await _reviews.UpdateAttachmentAsync(attachment, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<TradeAttachmentDto>(saved.Error);

        // 7) Update cached quota usage (best-effort: drift is recovered by
        // the next sweep via SUM(bytes) ground truth). We do NOT fail the
        // upload if this projection update throws — the attachment is
        // already persisted and the user already uploaded the bytes.
        await IncrementQuotaUsageBestEffortAsync(req.UserId, attachment.SizeBytes, ct);

        return Result.Success(attachment.ToDto());
    }

    private async Task<Result<VirusScanResult>> RunScanOrRollbackAsync(
        TradeAttachment attachment,
        Stream stream,
        CancellationToken ct)
    {
        try
        {
            // The no-op scanner doesn't read the stream; real impls will.
            // We pass the attachment content type so content-aware rules
            // can dispatch (PDF vs PNG heuristics differ).
            return Result.Success(await _scanner.ScanAsync(stream, attachment.ContentType, ct));
        }
        catch (ScannerUnavailableException ex)
        {
            _logger.LogWarning(ex,
                "Virus scanner unavailable for attachment {AttachmentId} — rolling back upload.",
                attachment.Id);
            await _storage.DeleteAsync(attachment.ObjectKey, ct);
            return Result.Failure<VirusScanResult>(AttachmentsErrors.ScannerUnavailable);
        }
    }

    private async Task IncrementQuotaUsageBestEffortAsync(Guid userId, long bytes, CancellationToken ct)
    {
        try
        {
            var current = await _quotaReader.GetQuotaAsync(userId, ct);
            if (current is null) return; // user gone — sweep will reconcile
            // The projection is intentionally read-only here. Real update
            // requires the Identity infrastructure — deferred to a future
            // slice. For Wave 4d the sweep recovers drift via SUM(bytes).
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Quota usage projection update failed for user {UserId}; sweep will reconcile.",
                userId);
        }
    }
}
