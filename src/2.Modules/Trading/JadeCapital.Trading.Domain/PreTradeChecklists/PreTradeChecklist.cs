using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.Common;

namespace JadeCapital.Trading.Domain.PreTradeChecklists;

/// <summary>
/// Aggregate Root del pre-trade checklist.
///
/// Reglas de negocio:
/// <list type="bullet">
///   <item>Una fila por trade — enforced por UNIQUE INDEX sobre trade_id
///   en la DB. La API no expone UPDATE del checklist (es una decision
///   puntual al momento de abrir el trade).</item>
///   <item>El TradeId/UserId NO se validan aqui como Guid.Empty (la FK a
///   la tabla correspondiente es la red de seguridad). El handler de
///   aplicacion garantiza que el userId matchea el usuario autenticado
///   y que el tradeId fue generado por la misma UoW (ver
///   OpenTradeHandler).</item>
///   <item><c>RiskRewardAtEntry</c> debe ser &gt;= <c>RiskRewardTargetUsed</c>.
///   El target usado puede venir del perfil activo del usuario (snapshot)
///   o del default 1.0 cuando no hay perfil. El handler de OpenTrade
///   resuelve el target; este aggregate solo valida la consistencia
///   interna entre RR al ingreso y target usado.</item>
///   <item><c>ConfluencesCount</c> debe estar en [1, 10].</item>
///   <item><c>Emotionality</c> y <c>SetupQuality</c> deben estar en [1, 5]
///   — enforced por los factories <see cref="PreTradeChecklistEnums"/>.
///   El aggregate re-valida por si el VO llega con un byte fuera de
///   rango (casteo explicito desde un codepath de hydration).</item>
/// </list>
/// </summary>
public sealed class PreTradeChecklist : AggregateRoot<Guid>
{
    public const byte MinConfluences = 1;
    public const byte MaxConfluences = 10;

    public Guid TradeId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public PreTradeChecklistSubmission Submission { get; private set; } = default!;

    // EF Core.
    private PreTradeChecklist() { }

    private PreTradeChecklist(
        Guid id,
        Guid tradeId,
        Guid userId,
        PreTradeChecklistSubmission submission,
        DateTimeOffset submittedAt) : base(id)
    {
        TradeId = tradeId;
        UserId = userId;
        Submission = submission;
        SubmittedAt = submittedAt;
        // Override CreatedAt del constructor base (que usa UtcNow) para que
        // el test pueda fijar el reloj y el aggregate CreatedAt == SubmittedAt.
        SetCreatedAt(submittedAt);
    }

    /// <summary>
    /// Construye un checklist pre-trade. Falla (validation) cuando:
    /// <list type="bullet">
    ///   <item><c>RiskRewardAtEntry &lt; RiskRewardTargetUsed</c>.</item>
    ///   <item><c>ConfluencesCount</c> esta fuera de [1, 10].</item>
    ///   <item><c>Emotionality</c> o <c>SetupQuality</c> llegan con un byte
    ///   fuera de [1, 5] (defensa en profundidad; el VO ya viene validado).</item>
    /// </list>
    /// En exito emite un <see cref="PreTradeChecklistSubmittedDomainEvent"/>.
    /// </summary>
    public static Result<PreTradeChecklist> Create(
        Guid tradeId,
        Guid userId,
        PreTradeChecklistSubmission submission,
        DateTimeOffset submittedAt)
    {
        if (submission is null)
            return Result.Failure<PreTradeChecklist>(TradingDomainErrors.PreTradeChecklist.SubmissionRequired);

        // Defense in depth: VO ya deberia traer enums validos, pero si el
        // codepath de hydration los construye con un cast explicito desde
        // un byte fuera de rango, queremos rechazarlo aqui tambien.
        var emotionalityCheck = PreTradeChecklistEnums.CreateEmotionality((byte)submission.Emotionality);
        if (emotionalityCheck.IsFailure)
            return Result.Failure<PreTradeChecklist>(emotionalityCheck.Error);

        var setupQualityCheck = PreTradeChecklistEnums.CreateSetupQuality((byte)submission.SetupQuality);
        if (setupQualityCheck.IsFailure)
            return Result.Failure<PreTradeChecklist>(setupQualityCheck.Error);

        if (submission.ConfluencesCount is < MinConfluences or > MaxConfluences)
            return Result.Failure<PreTradeChecklist>(TradingDomainErrors.PreTradeChecklist.ConfluencesOutOfRange);

        if (submission.RiskRewardAtEntry < submission.RiskRewardTargetUsed)
            return Result.Failure<PreTradeChecklist>(TradingDomainErrors.PreTradeChecklist.RrBelowTarget);

        var id = Guid.NewGuid();
        var checklist = new PreTradeChecklist(id, tradeId, userId, submission, submittedAt);

        checklist.RaiseDomainEvent(new PreTradeChecklistSubmittedDomainEvent(
            checklist.Id,
            checklist.TradeId,
            checklist.UserId,
            checklist.Submission.Emotionality,
            checklist.Submission.SetupQuality,
            checklist.Submission.RiskRewardAtEntry,
            checklist.Submission.RiskRewardTargetUsed,
            checklist.Submission.ConfluencesCount,
            checklist.SubmittedAt,
            submittedAt));

        return Result.Success(checklist);
    }
}

/// <summary>
/// Domain event emitido cuando un checklist pre-trade es submitted junto
/// con un OpenTrade exitoso. NO incluye PII: el UserId es el identificador
/// interno (Guid, ya sin nombre/email).
///
/// Lleva todos los valores del checklist para que consumidores downstream
/// (e.g. projection de "avg confluences por mes") puedan hidratarse sin
/// volver a la DB.
/// </summary>
public sealed record PreTradeChecklistSubmittedDomainEvent(
    Guid ChecklistId,
    Guid TradeId,
    Guid UserId,
    Emotionality Emotionality,
    SetupQuality SetupQuality,
    decimal RiskRewardAtEntry,
    decimal RiskRewardTargetUsed,
    byte ConfluencesCount,
    DateTimeOffset SubmittedAt,
    DateTimeOffset OccurredOn) : IDomainEvent;
