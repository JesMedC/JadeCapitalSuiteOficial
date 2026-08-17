using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
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
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<ConfirmAttachmentUploadedHandler> _logger;

    public ConfirmAttachmentUploadedHandler(
        ITradeReviewRepository reviews,
        IAttachmentStorage storage,
        IUnitOfWork uow,
        IClock clock,
        ILogger<ConfirmAttachmentUploadedHandler> logger)
    {
        _reviews = reviews;
        _storage = storage;
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

        // 5) Mark uploaded + emit domain event (internamente).
        var uploadTime = _clock.UtcNow;
        var markResult = attachment.MarkUploaded(req.Sha256, tradeId ?? Guid.Empty, uploadTime);
        if (markResult.IsFailure)
            return Result.Failure<TradeAttachmentDto>(markResult.Error);

        await _reviews.UpdateAttachmentAsync(attachment, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<TradeAttachmentDto>(saved.Error);

        return Result.Success(attachment.ToDto());
    }
}
