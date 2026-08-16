using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.TradeAttachments;
using MediatR;

namespace JadeCapital.Trading.Application.Features.TradeReviews.RequestAttachmentUpload;

/// <summary>
/// Whitelist de content-types aceptados. Los attachments son screenshots
/// (PNG/JPEG/WebP) o PDFs cortos (análisis previos al trade, etc.).
/// Videos, archives comprimidos o ejecutables no entran.
///
/// Es un set chico y cerrado para que el bucket no se llene de basura
/// (un atacante autenticado podría subir cualquier cosa a un bucket
/// abierto). Si en el futuro queremos más tipos, se agrega acá y se
/// redeploy.
/// </summary>
public static class AttachmentContentTypes
{
    public static readonly IReadOnlySet<string> Allowed =
        new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "image/png",
            "image/jpeg",
            "image/webp",
            "application/pdf",
        };

    public static bool IsAllowed(string contentType) => Allowed.Contains(contentType);
}

/// <summary>
/// Comando para pedir un slot de attachment en el review de un trade
/// cerrado. Devuelve el ID del attachment (pending) + URL presignada para
/// que el cliente suba los bytes directamente a MinIO.
///
/// <c>TradeId</c> es lo que el FE conoce (el review es per-trade 1:1;
/// no exponemos reviewId por separado). El handler resuelve el review
/// internamente.
///
/// Validations:
/// <list type="bullet">
///   <item><c>contentType</c> debe estar en la whitelist.</item>
///   <item><c>sizeBytes</c> entre 1 y 10 MB (DB + domain ya enforce, pero
///   el handler revalida para respuesta rapida).</item>
///   <item>El trade debe existir, ser del user, estar Closed, y tener
///   review creado (sino 404).</item>
/// </list>
/// </summary>
public sealed record RequestAttachmentUploadCommand(
    Guid TradeId,
    Guid UserId,
    string ContentType,
    long SizeBytes,
    string? Filename) : IRequest<Result<RequestAttachmentUploadResultDto>>;

public sealed class RequestAttachmentUploadValidator : AbstractValidator<RequestAttachmentUploadCommand>
{
    public RequestAttachmentUploadValidator()
    {
        RuleFor(x => x.TradeId).NotEqual(Guid.Empty);
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.ContentType)
            .Must(AttachmentContentTypes.IsAllowed)
            .WithMessage("Content type must be image/png, image/jpeg, image/webp, or application/pdf.");
        RuleFor(x => x.SizeBytes)
            .InclusiveBetween(TradeAttachment.MinSizeBytes, TradeAttachment.MaxSizeBytes);
        // Filename es opcional; si viene, saneamos longitud para no persistir
        // un filename gigante en el object_key.
        RuleFor(x => x.Filename).MaximumLength(255);
    }
}

public sealed class RequestAttachmentUploadHandler
    : IRequestHandler<RequestAttachmentUploadCommand, Result<RequestAttachmentUploadResultDto>>
{
    /// <summary>Tiempo de vida del presigned URL. El cliente debe usarlo
    /// dentro de esta ventana o volver a pedir otro slot.</summary>
    private static readonly TimeSpan PresignedUrlTtl = TimeSpan.FromMinutes(15);

    /// <summary>Max attachments permitidos por review. 5 es razonable
    /// (imagen del setup + imagen del resultado + chart + 2 PDFs).</summary>
    private const int MaxAttachmentsPerReview = 5;

    private readonly ITradeReviewRepository _reviews;
    private readonly IAttachmentStorage _storage;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;

    public RequestAttachmentUploadHandler(
        ITradeReviewRepository reviews,
        IAttachmentStorage storage,
        IUnitOfWork uow,
        IClock clock)
    {
        _reviews = reviews;
        _storage = storage;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<RequestAttachmentUploadResultDto>> Handle(
        RequestAttachmentUploadCommand req,
        CancellationToken ct)
    {
        // 1) Resolver el review (cross-user scope built-in: si el trade
        // no es del user, FindByTradeIdAsync retorna null).
        var review = await _reviews.FindByTradeIdAsync(req.TradeId, req.UserId, ct);
        if (review is null)
            return Result.Failure<RequestAttachmentUploadResultDto>(
                TradingDomainErrors.TradeReview.NotFound);

        // 2) Cap de attachments por review: 5. Si el usuario quiere mas,
        // debe borrar uno. Esto evita que un review se convierta en un
        // backup gratuito de MinIO.
        var existingCount = await _reviews.CountAttachmentsByReviewIdAsync(review.Id, ct);
        if (existingCount >= MaxAttachmentsPerReview)
            return Result.Failure<RequestAttachmentUploadResultDto>(
                Error.Failure("trade_review.too_many_attachments",
                    $"A review can have at most {MaxAttachmentsPerReview} attachments."));

        // 3) Construir el attachment aggregate (validations delegadas al
        // factory: sizeBytes, contentType format, etc.).
        var attachmentId = Guid.NewGuid();
        var trade = review.TradeId;
        var objectKey = BuildObjectKey(req.UserId, trade, review.Id, attachmentId, req.Filename);

        var slotResult = TradeAttachment.RequestSlot(
            id: attachmentId,
            reviewId: review.Id,
            userId: req.UserId,
            objectKey: objectKey,
            contentType: req.ContentType.Trim(),
            sizeBytes: req.SizeBytes,
            now: _clock.UtcNow);

        if (slotResult.IsFailure)
            return Result.Failure<RequestAttachmentUploadResultDto>(slotResult.Error);

        var attachment = slotResult.Value;

        // 4) Pedirle al storage la URL presignada.
        var putUrl = await _storage.GetPresignedPutUrlAsync(
            objectKey, PresignedUrlTtl, ct);

        // 5) Persistir el attachment (status = pending).
        await _reviews.AddAttachmentAsync(attachment, ct);
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<RequestAttachmentUploadResultDto>(saved.Error);

        return Result.Success(new RequestAttachmentUploadResultDto(
            AttachmentId: attachment.Id,
            ObjectKey: attachment.ObjectKey,
            PutUrl: putUrl,
            ExpiresInSeconds: (int)PresignedUrlTtl.TotalSeconds));
    }

    private static string BuildObjectKey(
        Guid userId, Guid tradeId, Guid reviewId, Guid attachmentId, string? filename)
    {
        // Sanitize filename: solo el basename y chars seguros. Evita que un
        // FE malicioso inyecte "../../" y escape del prefix de bucket.
        var safeFilename = string.IsNullOrWhiteSpace(filename)
            ? $"upload-{attachmentId:N}"
            : SanitizeForObjectKey(filename);

        return $"trading/attachments/{userId}/{tradeId}/{reviewId}/{attachmentId}/{safeFilename}";
    }

    private static string SanitizeForObjectKey(string filename)
    {
        // Strip path components, keep only the basename.
        var basename = System.IO.Path.GetFileName(filename.Trim());
        if (string.IsNullOrWhiteSpace(basename)) return "upload.bin";

        // Replace any char outside [A-Za-z0-9._-] with underscore. Esto
        // cubre acentos, espacios, slashes residuales, etc.
        var chars = basename.Select(c =>
            char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        return new string(chars.ToArray());
    }
}
