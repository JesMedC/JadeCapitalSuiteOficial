using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.TradeReviews.DeleteAttachment;

/// <summary>
/// Borra un attachment. El handler:
/// <list type="number">
///   <item>Resuelve el attachment (cross-user scope: 404 si no es del user).</item>
///   <item>Borra la fila de la DB.</item>
///   <item>Best-effort: borra el object de MinIO. Si falla, NO se reporta
///   al cliente — el object quedara huerfano en el bucket y un garbage
///   collector periodico (fuera del scope de v1) lo limpiara. La fila
///   ya esta borrada, asi que el cliente no la ve mas.</item>
/// </list>
/// </summary>
public sealed record DeleteAttachmentCommand(Guid AttachmentId, Guid UserId)
    : IRequest<Result>;

public sealed class DeleteAttachmentValidator : AbstractValidator<DeleteAttachmentCommand>
{
    public DeleteAttachmentValidator()
    {
        RuleFor(x => x.AttachmentId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
    }
}

public sealed class DeleteAttachmentHandler
    : IRequestHandler<DeleteAttachmentCommand, Result>
{
    private readonly ITradeReviewRepository _reviews;
    private readonly IAttachmentStorage _storage;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<DeleteAttachmentHandler> _logger;

    public DeleteAttachmentHandler(
        ITradeReviewRepository reviews,
        IAttachmentStorage storage,
        IUnitOfWork uow,
        ILogger<DeleteAttachmentHandler> logger)
    {
        _reviews = reviews;
        _storage = storage;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result> Handle(
        DeleteAttachmentCommand req,
        CancellationToken ct)
    {
        // 1) Resolve + cross-user scope.
        var objectKey = await _reviews.RemoveAttachmentAsync(req.AttachmentId, req.UserId, ct);
        if (objectKey is null)
            return Result.Failure(TradingDomainErrors.TradeAttachment.NotFound);

        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure(saved.Error);

        // 2) Best-effort storage cleanup. Loggeamos el fallo pero no
        // rompemos el response — la fila de DB ya esta borrada.
        try
        {
            await _storage.DeleteAsync(objectKey, ct);
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex,
                "Best-effort MinIO delete failed for object key {ObjectKey} (orphan).",
                objectKey);
        }

        return Result.Success();
    }
}
