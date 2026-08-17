using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Storage;
using JadeCapital.Trading.Domain.Common;
using JadeCapital.Trading.Domain.TradeReviews;

namespace JadeCapital.Trading.Domain.TradeAttachments;

/// <summary>
/// Aggregate Root del attachment de un post-trade review (slice 1d.1).
///
/// Reglas de negocio:
/// <list type="bullet">
///   <item>El <c>objectKey</c> SIEMPRE tiene el shape
///   <c>trading/attachments/{userId}/{tradeId}/{reviewId}/{attachmentId}/{filename}</c>.
///   El handler de aplicacion lo construye; este agregado solo lo valida
///   y persiste.</item>
///   <item>El tamano esta acotado: 1 byte <= sizeBytes <= 10 MB. La DB
///   enforce lo mismo con un CHECK; el agregado enforce el rango en
///   factory para feedback rapido.</item>
///   <item><c>status</c> transita pending -> (uploaded | failed). No se
///   permite re-transitar ni "volver atras".</item>
///   <item>El cross-user scope lo enforce el handler: el handler rechaza
///   con 404 si el review no es del userId autenticado, antes de que el
///   factory reciba el payload.</item>
/// </list>
/// </summary>
public sealed class TradeAttachment : AggregateRoot<Guid>
{
    /// <summary>Tamano minimo: 1 byte (cero bytes es archivo vacio, no permitido).</summary>
    public const long MinSizeBytes = 1L;

    /// <summary>Tamano maximo: 10 MB (10485760 bytes).</summary>
    public const long MaxSizeBytes = 10L * 1024L * 1024L;

    public Guid ReviewId { get; private set; }
    public Guid UserId { get; private set; }
    public string ObjectKey { get; private set; } = default!;
    public string ContentType { get; private set; } = default!;
    public long SizeBytes { get; private set; }
    public string? Sha256 { get; private set; }
    public TradeAttachmentStatus Status { get; private set; }
    public DateTimeOffset? UploadedAt { get; private set; }

    /// <summary>
    /// Slice 4d — virus scan metadata. Defaults to
    /// <see cref="VirusScanResult.NotScanned"/> for legacy rows created
    /// before Wave 4d. The ConfirmAttachmentUploadedHandler stamps
    /// these via <see cref="ApplyScanResult"/>.
    /// </summary>
    public VirusScanResult ScanResult { get; private set; } = VirusScanResult.NotScanned;
    public DateTimeOffset? VirusScannedAt { get; private set; }

    /// <summary>
    /// Slice 4d — expiration timestamp set at confirmation time as
    /// <c>confirmed_at + 90 days</c>. The daily lifecycle sweep soft-deletes
    /// rows where <c>expires_at &lt; now()</c>. NULL = legacy row, never
    /// swept.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>
    /// Slice 4d — MinIO object key for the cached thumbnail (image
    /// attachments only). Populated by a future thumbnail-generation job;
    /// the GetThumbnailHandler uses the raw <see cref="ObjectKey"/> +
    /// ?width= query params for now.
    /// </summary>
    public string? ThumbnailObjectKey { get; private set; }

    /// <summary>
    /// Soft-delete flag. The AttachmentLifecycleService daily sweep
    /// flips this to <c>false</c> when <see cref="ExpiresAt"/> &lt; now().
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Timestamp the row was soft-deleted by the sweep. NULL until
    /// the sweep processes this attachment.
    /// </summary>
    public DateTimeOffset? SweptAt { get; private set; }

    // EF Core.
    private TradeAttachment() { }

    private TradeAttachment(
        Guid id,
        Guid reviewId,
        Guid userId,
        string objectKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset createdAt) : base(id)
    {
        ReviewId = reviewId;
        UserId = userId;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Status = TradeAttachmentStatus.Pending;
        SetCreatedAt(createdAt);
    }

    /// <summary>
    /// Solicita un slot para un attachment. Crea una fila en estado
    /// <c>Pending</c>; el cliente debe subir los bytes a MinIO directamente
    /// usando la presigned URL y luego llamar a <see cref="MarkUploaded"/>
    /// (vía <c>CompleteAttachmentUploadHandler</c>).
    /// </summary>
    public static Result<TradeAttachment> RequestSlot(
        Guid id,
        Guid reviewId,
        Guid userId,
        string objectKey,
        string contentType,
        long sizeBytes,
        DateTimeOffset now)
    {
        if (id == Guid.Empty)
            return Result.Failure<TradeAttachment>(TradingDomainErrors.TradeAttachment.IdRequired);

        if (reviewId == Guid.Empty)
            return Result.Failure<TradeAttachment>(TradingDomainErrors.TradeAttachment.ReviewIdRequired);

        if (userId == Guid.Empty)
            return Result.Failure<TradeAttachment>(TradingDomainErrors.TradeAttachment.UserIdRequired);

        if (string.IsNullOrWhiteSpace(objectKey))
            return Result.Failure<TradeAttachment>(TradingDomainErrors.TradeAttachment.ObjectKeyRequired);

        if (string.IsNullOrWhiteSpace(contentType))
            return Result.Failure<TradeAttachment>(TradingDomainErrors.TradeAttachment.ContentTypeRequired);

        if (sizeBytes is < MinSizeBytes or > MaxSizeBytes)
            return Result.Failure<TradeAttachment>(TradingDomainErrors.TradeAttachment.SizeOutOfRange);

        var attachment = new TradeAttachment(
            id, reviewId, userId, objectKey.Trim(), contentType.Trim(), sizeBytes, now);

        return Result.Success(attachment);
    }

    /// <summary>
    /// Marca el attachment como uploaded despues de que el backend confirmo
    /// que el objeto existe en MinIO con el tamano (y sha256 si fue
    /// provisto) esperado. Solo valido desde estado Pending.
    ///
    /// <paramref name="tradeId"/> se pasa como parametro porque el aggregate
    /// solo conoce su <see cref="ReviewId"/> (el TradeId es una FK del review
    /// padre, NO navigation property en el domain). Es necesario para emitir
    /// el <see cref="TradeAttachmentUploadedDomainEvent"/> con el TradeId en
    /// el payload — el spec exige que el evento incluya el campo.
    /// </summary>
    public Result MarkUploaded(string? sha256, Guid tradeId, DateTimeOffset now)
    {
        if (Status == TradeAttachmentStatus.Uploaded)
            return Result.Failure(TradingDomainErrors.TradeAttachment.AlreadyUploaded);

        if (Status == TradeAttachmentStatus.Failed)
            return Result.Failure(TradingDomainErrors.TradeAttachment.AlreadyFailed);

        if (!string.IsNullOrEmpty(sha256)
            && !System.Text.RegularExpressions.Regex.IsMatch(sha256, "^[a-fA-F0-9]{64}$"))
            return Result.Failure(TradingDomainErrors.TradeAttachment.Sha256ShapeInvalid);

        Sha256 = string.IsNullOrEmpty(sha256) ? null : sha256.ToLowerInvariant();
        Status = TradeAttachmentStatus.Uploaded;
        UploadedAt = now;
        Touch();

        RaiseDomainEvent(new TradeAttachmentUploadedDomainEvent(
            Id, ReviewId, tradeId, UserId,
            ObjectKey, ContentType, SizeBytes, now));

        return Result.Success();
    }

    /// <summary>
    /// Marca el attachment como failed. Estado terminal; el cliente puede
    /// resubir si quiere creando un NUEVO attachment slot.
    /// </summary>
    public Result MarkFailed(DateTimeOffset now)
    {
        if (Status == TradeAttachmentStatus.Uploaded)
            return Result.Failure(TradingDomainErrors.TradeAttachment.AlreadyUploaded);

        if (Status == TradeAttachmentStatus.Failed)
            return Result.Failure(TradingDomainErrors.TradeAttachment.AlreadyFailed);

        Status = TradeAttachmentStatus.Failed;
        Touch();

        return Result.Success();
    }

    /// <summary>
    /// Slice 4d — stamps the virus-scan + lifecycle fields. Called by
    /// <c>ConfirmAttachmentUploadedHandler</c> right before
    /// <see cref="MarkUploaded"/>. Stores <paramref name="expiresAt"/> =
    /// <c>confirmed_at + ExpirationDays</c> for the daily sweep.
    /// </summary>
    public void ApplyScanResult(VirusScanResult result, DateTimeOffset scannedAt, DateTimeOffset expiresAt)
    {
        ScanResult = result;
        VirusScannedAt = scannedAt;
        ExpiresAt = expiresAt;
        Touch();
    }

    /// <summary>
    /// Slice 4d — soft-delete invoked by the daily sweep when
    /// <see cref="ExpiresAt"/> &lt; now. Idempotent: re-running on an
    /// already-swept row is a no-op. Does NOT touch the upload status
    /// (the row remains 'uploaded' for audit purposes; only the
    /// <see cref="IsActive"/> flag flips).
    /// </summary>
    public Result MarkSwept(DateTimeOffset sweptAt)
    {
        if (!IsActive) return Result.Success();
        IsActive = false;
        SweptAt = sweptAt;
        Touch();
        return Result.Success();
    }
}

/// <summary>
/// Domain event emitido cuando un attachment se marca como uploaded.
/// NO incluye el sha256, bytes ni presigned URL (consumidores pueden leer
/// el aggregate). Solo los campos NO sensibles + identificadores.
///
/// Sin embargo, el spec requiere que NO se incluya sha256 en el event body
/// (solo attachmentId, objectKey, sizeBytes, contentType). Como el
/// aggregate expone <c>ObjectKey</c> y <c>ContentType</c> y <c>SizeBytes</c>
/// sin exponer sha256, el event cumple el contrato.
/// </summary>
public sealed record TradeAttachmentUploadedDomainEvent(
    Guid AttachmentId,
    Guid ReviewId,
    Guid TradeId,
    Guid UserId,
    string ObjectKey,
    string ContentType,
    long SizeBytes,
    DateTimeOffset OccurredOn) : IDomainEvent;
