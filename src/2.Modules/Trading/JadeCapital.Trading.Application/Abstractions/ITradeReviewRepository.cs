using JadeCapital.Trading.Domain.TradeAttachments;
using JadeCapital.Trading.Domain.TradeReviews;

namespace JadeCapital.Trading.Application.Abstractions;

/// <summary>
/// Contrato de persistencia para los aggregates de review y attachment
/// del slice 1d.1.
///
/// Implementacion EF Core en <c>Trading.Infrastructure</c>. La API es
/// minima: el review es essentially read + upsert, y el attachment es
/// append + status update + delete. NO exponemos navegaciones lazy: el
/// handler pregunta por review/attachments cuando los necesita y los
/// pasamos explicitamente al mapper.
///
/// El cross-user scope NO se enforce aqui — eso es responsabilidad del
/// handler (todos los FindByXAsync reciben userId como parametro y la
/// query filtra por userId).
/// </summary>
public interface ITradeReviewRepository
{
    /// <summary>Inserta un review nuevo. Falla con <c>Conflict</c> si ya
    /// existe un review para ese trade (UNIQUE INDEX DB layer).</summary>
    Task AddAsync(TradeReview review, CancellationToken ct);

    /// <summary>Busca el review de un trade (FK unique). Retorna null si
    /// no existe. Si existe y NO pertenece al userId, retorna null
    /// (cross-user scope).</summary>
    Task<TradeReview?> FindByTradeIdAsync(Guid tradeId, Guid userId, CancellationToken ct);

    /// <summary>Busca un review por id. Si NO pertenece al userId,
    /// retorna null (cross-user scope).</summary>
    Task<TradeReview?> FindByIdAsync(Guid reviewId, Guid userId, CancellationToken ct);

    /// <summary>Marca el review como Updated (UpdatedAt). Usado por
    /// <c>CreateOrUpdateTradeReviewHandler</c> cuando hace upsert.</summary>
    Task UpdateAsync(TradeReview review, CancellationToken ct);

    /// <summary>Inserta un attachment en estado pending. La fila es
    /// posterior al slot request.</summary>
    Task AddAttachmentAsync(TradeAttachment attachment, CancellationToken ct);

    /// <summary>Lista todos los attachments de un review. Orden por
    /// createdAt asc para que el FE los muestre en orden de subida.</summary>
    Task<IReadOnlyList<TradeAttachment>> ListAttachmentsByReviewIdAsync(Guid reviewId, Guid userId, CancellationToken ct);

    /// <summary>Cuenta los attachments pending de un review (para el cap
    /// de "max attachments per review").</summary>
    Task<int> CountAttachmentsByReviewIdAsync(Guid reviewId, CancellationToken ct);

    /// <summary>Busca un attachment por id. Si NO pertenece al userId,
    /// retorna null.</summary>
    Task<TradeAttachment?> FindAttachmentByIdAsync(Guid attachmentId, Guid userId, CancellationToken ct);

    /// <summary>Resuelve el TradeId (del review padre) del attachment. Lo
    /// usa <c>ConfirmAttachmentUploadedHandler</c> para emitir el evento
    /// con el campo TradeId poblado.</summary>
    Task<Guid?> GetTradeIdByAttachmentIdAsync(Guid attachmentId, CancellationToken ct);

    /// <summary>Marca el attachment como uploaded (status flip).</summary>
    Task UpdateAttachmentAsync(TradeAttachment attachment, CancellationToken ct);

    /// <summary>Borra un attachment. Retorna el object_key borrado (para
    /// que el handler limpie el object de MinIO best-effort), o null si
    /// no se encontro.</summary>
    Task<string?> RemoveAttachmentAsync(Guid attachmentId, Guid userId, CancellationToken ct);
}
