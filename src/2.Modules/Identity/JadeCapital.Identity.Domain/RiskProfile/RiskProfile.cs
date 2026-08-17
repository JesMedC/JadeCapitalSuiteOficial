using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.Domain.RiskProfile;

/// <summary>
/// Aggregate root del perfil de riesgo del usuario. Single-active por usuario:
/// exactamente una fila con <see cref="IsActive"/> = <c>true</c> en cualquier
/// momento (enforce por el UNIQUE INDEX PARTIAL <c>ux_risk_profiles_user_active</c>
/// en la DB y por el handler <c>CreateOrSupersedeRiskProfileHandler</c> que hace
/// el supersede + add en una sola UoW).
///
/// Reglas de negocio:
/// <list type="bullet">
///   <item>Capital es Money (NUMERIC(24,8) + ISO 4217-like). El VO Currency se
///   usa para enforce el formato a nivel dominio; el CHAR(3) + CHECK a
///   nivel DB redundan.</item>
///   <item>Drawdown / risk-per-trade / RR target son value objects con rangos
///   cerrados (<c>[0.00, 50.00]</c>, <c>[0.01, 5.00]</c>, <c>&gt;= 1.0</c>);
///   los rangos se enforce a tres niveles: VO Create, CHECK DB, y redaccion
///   ProblemDetails coherente en el endpoint.</item>
///   <item>MarkSuperseded es idempotente: si ya esta superseded, el call es
///   un no-op con Result.Success. Esto refleja el contrato de supersede
///   atomica al nivel del aggregate — la transaccion de DB (no este
///   metodo) es quien ensure la single-active invariant.</item>
///   <item>Update reemplaza valores en sitio sin crear un nuevo agregado.
///   Se usa unicamente en escenarios donde el perfil activo cambia sin
///   supersede (decision de Wave 1: en MVP el flujo canónico siempre hace
///   supersede + create, por lo que Update queda como API defensiva).</item>
/// </list>
/// </summary>
public sealed class RiskProfile : AggregateRoot<Guid>
{
    public Guid UserId { get; private set; }

    /// <summary>Capital = Amount + Currency. Mapeado via OwnsOne en EF.</summary>
    public Money Capital { get; private set; } = default!;

    /// <summary>Moneda derivada (no persistida, viene de Capital.CurrencyCode).</summary>
    public string CapitalCurrencyCode => Capital.CurrencyCode;
    public decimal CapitalAmount => Capital.Amount;

    public MaxDrawdownPercent MaxDrawdownPercent { get; private set; } = default!;
    public RiskPerTradePercent RiskPerTradePercent { get; private set; } = default!;
    public RiskRewardRatio RiskRewardTarget { get; private set; } = default!;

    public bool IsActive { get; private set; }
    public DateTimeOffset? SupersededAt { get; private set; }

    // EF Core.
    private RiskProfile() { }

    private RiskProfile(
        Guid id,
        Guid userId,
        Money capital,
        MaxDrawdownPercent maxDrawdown,
        RiskPerTradePercent riskPerTrade,
        RiskRewardRatio rrTarget,
        DateTimeOffset createdAt)
        : base(id)
    {
        UserId = userId;
        Capital = capital;
        MaxDrawdownPercent = maxDrawdown;
        RiskPerTradePercent = riskPerTrade;
        RiskRewardTarget = rrTarget;
        IsActive = true;
        SupersededAt = null;
        SetCreatedAt(createdAt);
    }

    /// <summary>
    /// Factory: crea un perfil activo. El supersede del perfil previo (si
    /// existe) NO es responsabilidad de este metodo — la capa de aplicacion
    /// <c>CreateOrSupersedeRiskProfileHandler</c> lo hace en la misma
    /// UoW antes de invocar AddAsync aqui.
    ///
    /// El caller debe garantizar:
    /// <list type="number">
    ///   <item>El id no es Guid.Empty.</item>
    ///   <item>El userId no es Guid.Empty.</item>
    ///   <item>Los VOs ya pasaron <c>Create()</c> (o vienen de FromTrusted
    ///   durante hydration). Esta factory NO los valida nuevamente.</item>
    /// </list>
    /// </summary>
    public static Result<RiskProfile> Create(
        Guid id,
        Guid userId,
        Money capital,
        MaxDrawdownPercent maxDrawdown,
        RiskPerTradePercent riskPerTrade,
        RiskRewardRatio rrTarget,
        IClock clock)
    {
        if (id == Guid.Empty)
            return Result.Failure<RiskProfile>(Identity.Domain.Common.IdentityDomainErrors.User.IdRequired);

        if (userId == Guid.Empty)
            return Result.Failure<RiskProfile>(Identity.Domain.Common.IdentityDomainErrors.User.IdRequired);

        if (capital is null)
            return Result.Failure<RiskProfile>(RiskProfileErrors.CapitalOutOfRange);

        if (capital.Amount <= 0)
            return Result.Failure<RiskProfile>(RiskProfileErrors.CapitalOutOfRange);

        var now = clock.UtcNow;
        var profile = new RiskProfile(id, userId, capital, maxDrawdown, riskPerTrade, rrTarget, now);
        profile.RaiseDomainEvent(new RiskProfileCreatedDomainEvent(profile.Id, profile.UserId, now));
        return Result.Success(profile);
    }

    /// <summary>
    /// Marca el perfil como superseded (isActive=FALSE, SupersededAt=now).
    /// Idempotente: si ya esta superseded, devuelve Result.Success sin
    /// mutar el timestamp ni emitir un nuevo evento. Esto mantiene el
    /// contrato del spec scenario "MarkSuperseded idempotency" y permite
    /// al handler aplicacion llamarlo repetidamente sin efecto secundario.
    /// </summary>
    public Result MarkSuperseded(IClock clock)
    {
        if (!IsActive) return Result.Success(); // no-op idempotente

        var now = clock.UtcNow;
        IsActive = false;
        SupersededAt = now;
        Touch();
        RaiseDomainEvent(new RiskProfileSupersededDomainEvent(Id, UserId, now));
        return Result.Success();
    }

    /// <summary>
    /// Reemplaza los valores del perfil en sitio sin cambiar id ni userId.
    /// Usado por el handler de actualizacion in-place; no emite
    /// supersede. El handler debe haber llamado <see cref="MarkSuperseded"/>
    /// antes si lo que queria era un supersede+create canonico.
    /// </summary>
    public Result Update(
        Money capital,
        MaxDrawdownPercent maxDrawdown,
        RiskPerTradePercent riskPerTrade,
        RiskRewardRatio rrTarget)
    {
        if (capital is null || capital.Amount <= 0)
            return Result.Failure(RiskProfileErrors.CapitalOutOfRange);

        if (maxDrawdown is null || riskPerTrade is null || rrTarget is null)
            return Result.Failure(Error.Validation(
                "risk_profile.invalid_value_objects",
                "Risk profile value objects are required."));

        Capital = capital;
        MaxDrawdownPercent = maxDrawdown;
        RiskPerTradePercent = riskPerTrade;
        RiskRewardTarget = rrTarget;
        Touch();
        RaiseDomainEvent(new RiskProfileUpdatedDomainEvent(Id, UserId, DateTimeOffset.UtcNow));
        return Result.Success();
    }
}
