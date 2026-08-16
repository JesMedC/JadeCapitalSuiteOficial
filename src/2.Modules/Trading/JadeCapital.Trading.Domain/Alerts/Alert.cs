using JadeCapital.Shared.Kernel.Coaching;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Common;
using DomainSeverity = JadeCapital.Trading.Domain.Behavioral.Severity;

namespace JadeCapital.Trading.Domain.Alerts;

// ============================================================================
//  Alert aggregate — slice 3b (Trader Strategies + Alerts + Planner).
//
//  Persistent alert emitted by an IAlertRule. Lives in trading.alerts with
//  one row per (user_id, rule_id, UTC-date) enforced by the partial unique
//  index (the BackgroundService dedups via ON CONFLICT DO NOTHING).
//
//  Reglas de negocio:
//   - title 1..MaxTitleLength chars; body 1..MaxBodyLength chars.
//   - ruleId non-empty (e.g. "NoTradesInDaysRule", "DrawdownExceededRule").
//   - severity ∈ {Low, Medium, High}. Mapped from domain Behavioral.Severity
//     at construction time so the aggregate stores the wire-level value.
//   - ctaRoute + ctaLabel form the CTA the FE renders (route is a frontend
//     path, not a backend API URL).
//   - expiresAt optional: when set, the API filters the alert out of
//     ?activeOnly=true once expiresAt < now().
//   - acknowledgedAt optional: when set, the alert was acked by the user.
//     Idempotent Acknowledge() — second call is a no-op preserving the
//     original timestamp (per spec scenario "Ack idempotent").
//
//  Invariantes:
//   - Id != Guid.Empty (assigned at Create).
//   - UserId != Guid.Empty.
//   - ruleId non-empty.
//   - title non-empty, <= MaxTitleLength chars.
//   - body non-empty, <= MaxBodyLength chars.
//   - severity byte 1..3.
// ============================================================================

public sealed class Alert : AggregateRoot<Guid>
{
    /// <summary>Max length of the alert title.</summary>
    public const int MaxTitleLength = 120;

    /// <summary>Max length of the alert body (PII-safe copy + CTA hint).</summary>
    public const int MaxBodyLength = 500;

    /// <summary>Max length of ruleId (matches VARCHAR(64) in the migration).</summary>
    public const int MaxRuleIdLength = 64;

    public Guid UserId { get; private set; }
    public string RuleId { get; private set; } = default!;
    public Severity Severity { get; private set; }
    public string Title { get; private set; } = default!;
    public string Body { get; private set; } = default!;
    public string CtaRoute { get; private set; } = default!;
    public string CtaLabel { get; private set; } = default!;
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public new DateTimeOffset CreatedAt { get; private set; }

    // EF Core.
    private Alert() { }

    private Alert(
        Guid id,
        Guid userId,
        string ruleId,
        Severity severity,
        string title,
        string body,
        string ctaRoute,
        string ctaLabel,
        DateTimeOffset? expiresAt,
        IClock clock) : base(id)
    {
        UserId = userId;
        RuleId = ruleId;
        Severity = severity;
        Title = title;
        Body = body;
        CtaRoute = ctaRoute;
        CtaLabel = ctaLabel;
        ExpiresAt = expiresAt;
        CreatedAt = clock.UtcNow;
    }

    /// <summary>
    /// Builds a new Alert for the given user. Validates invariants before
    /// constructing; returns a Failure with a stable error code otherwise.
    /// </summary>
    public static Result<Alert> Create(
        Guid userId,
        string ruleId,
        DomainSeverity severity,
        string title,
        string body,
        string ctaRoute,
        string ctaLabel,
        DateTimeOffset? expiresAt,
        IClock clock)
    {
        if (userId == Guid.Empty)
            return Result.Failure<Alert>(TradingDomainErrors.Alert.UserIdRequired);

        if (string.IsNullOrWhiteSpace(ruleId))
            return Result.Failure<Alert>(TradingDomainErrors.Alert.RuleIdRequired);

        var trimmedRuleId = ruleId.Trim();
        if (trimmedRuleId.Length > MaxRuleIdLength)
            return Result.Failure<Alert>(TradingDomainErrors.Alert.RuleIdTooLong);

        if (string.IsNullOrWhiteSpace(title))
            return Result.Failure<Alert>(TradingDomainErrors.Alert.TitleRequired);

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaxTitleLength)
            return Result.Failure<Alert>(TradingDomainErrors.Alert.TitleTooLong);

        if (string.IsNullOrWhiteSpace(body))
            return Result.Failure<Alert>(TradingDomainErrors.Alert.BodyRequired);

        var trimmedBody = body.Trim();
        if (trimmedBody.Length > MaxBodyLength)
            return Result.Failure<Alert>(TradingDomainErrors.Alert.BodyTooLong);

        var ctaRouteResult = NormalizeCtaRoute(ctaRoute);
        if (ctaRouteResult.IsFailure)
            return Result.Failure<Alert>(ctaRouteResult.Error);

        var ctaLabelResult = NormalizeCtaLabel(ctaLabel);
        if (ctaLabelResult.IsFailure)
            return Result.Failure<Alert>(ctaLabelResult.Error);

        var id = Guid.NewGuid();
        return Result.Success(new Alert(
            id, userId, trimmedRuleId,
            (Severity)(byte)severity,
            trimmedTitle, trimmedBody,
            ctaRouteResult.Value, ctaLabelResult.Value,
            expiresAt, clock));
    }

    /// <summary>
    /// Hidratacion directa desde la DB (EF rehydration path). NO valida
    /// longitudes (la DB enforce via VARCHAR caps; los datos persistidos
    /// son por definicion validos). Usar SOLO desde AlertRepository.
    /// </summary>
    public static Alert Rehydrate(
        Guid id,
        Guid userId,
        string ruleId,
        DomainSeverity severity,
        string title,
        string body,
        string ctaRoute,
        string ctaLabel,
        DateTimeOffset? acknowledgedAt,
        DateTimeOffset? expiresAt,
        DateTimeOffset createdAt)
    {
        return new Alert(
            id, userId, ruleId,
            (Severity)(byte)severity,
            title, body,
            ctaRoute, ctaLabel,
            expiresAt,
            new TrustedRehydrationClock(createdAt))
        {
            AcknowledgedAt = acknowledgedAt,
        };
    }

    /// <summary>
    /// Marks the alert as acknowledged at the given clock time. Idempotent:
    /// a second call preserves the original <see cref="AcknowledgedAt"/>.
    /// </summary>
    public Result Acknowledge(IClock clock)
    {
        if (AcknowledgedAt is not null)
            return Result.Success(); // idempotent

        AcknowledgedAt = clock.UtcNow;
        return Result.Success();
    }

    /// <summary>True when the alert should appear in the active list.</summary>
    public bool IsActive(DateTimeOffset now)
        => AcknowledgedAt is null
           && (ExpiresAt is null || ExpiresAt > now);

    // ===== Helpers =====

    private static Result<string> NormalizeCtaRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return Result.Failure<string>(TradingDomainErrors.Alert.CtaRouteRequired);

        var trimmed = route.Trim();
        if (!trimmed.StartsWith('/'))
            return Result.Failure<string>(TradingDomainErrors.Alert.CtaRouteInvalid);

        return Result.Success(trimmed);
    }

    private static Result<string> NormalizeCtaLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return Result.Failure<string>(TradingDomainErrors.Alert.CtaLabelRequired);

        var trimmed = label.Trim();
        if (trimmed.Length > 64)
            return Result.Failure<string>(TradingDomainErrors.Alert.CtaLabelTooLong);

        return Result.Success(trimmed);
    }

    /// <summary>
    /// Clock fake que solo se usa en el path de rehidratacion para que
    /// el constructor de <see cref="Alert"/> reciba un IClock y pueda
    /// setear CreatedAt. La fecha real viene de la DB y se asigna explicitamente.
    /// </summary>
    private sealed class TrustedRehydrationClock : IClock
    {
        private readonly DateTimeOffset _now;
        public TrustedRehydrationClock(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }
}