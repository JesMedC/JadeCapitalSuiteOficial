using JadeCapital.Shared.Kernel.Primitives;

namespace JadeCapital.Trading.Domain.AttachmentAudits;

/// <summary>
/// Aggregate root del audit del daily lifecycle sweep (slice 4d, Wave 4).
///
/// Una fila por usuario por corrida del sweep. Si el sweep no encontro
/// expired rows para un user (caso comun), la fila igual se inscribe con
/// <c>cleanedCount = 0</c> + <c>skippedReason = "no_expired"</c> para que
/// el dev pueda ver "el sweep corrio y vio este user".
///
/// Si el sweep fallo (Transient MinIO error, DB timeout, etc.) se inscribe
/// <c>errorMessage</c> en vez de tirar excepcion — la siguiente corrida
/// reintenta idempotente.
///
/// El aggregate NO tiene comportamiento (es solo audit / log). Se
/// construye via factory estatico para forzar el poblado de los campos
/// non-null (cleanedCount, ranAt).
/// </summary>
public sealed class AttachmentQuotaAudit : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }
    public DateTimeOffset RanAt { get; private set; }
    public int CleanedCount { get; private set; }
    public long CleanedBytes { get; private set; }
    public int RemainingCount { get; private set; }
    public long RemainingBytes { get; private set; }
    public string? SkippedReason { get; private set; }
    public string? ErrorMessage { get; private set; }

    // EF Core.
    private AttachmentQuotaAudit() { }

    public AttachmentQuotaAudit(
        Guid id,
        Guid userId,
        DateTimeOffset ranAt,
        int cleanedCount,
        long cleanedBytes,
        int remainingCount,
        long remainingBytes,
        string? skippedReason,
        string? errorMessage) : base(id)
    {
        UserId = userId;
        RanAt = ranAt;
        CleanedCount = cleanedCount;
        CleanedBytes = cleanedBytes;
        RemainingCount = remainingCount;
        RemainingBytes = remainingBytes;
        SkippedReason = skippedReason;
        ErrorMessage = errorMessage;
        SetCreatedAt(ranAt);
    }
}