using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Application.Features.RiskProfileActions.CreateOrSupersedeRiskProfile;

/// <summary>
/// MediatR handler para <see cref="CreateOrSupersedeRiskProfileCommand"/>.
/// Slice 1a.1a.
///
/// Flujo:
/// <list type="number">
///   <item>Validar capital + moneda (early-exit con codigos <c>validation.</c>
///   antes de tocar el repo).</item>
///   <item>Validar VOs (drawdown / risk-per-trade / RR target). Si alguno
///   falla, devolver <c>Result.Failure</c> sin persistir.</item>
///   <item>Buscar perfil activo del usuario (<c>IRiskProfileRepository.GetActiveAsync</c>).</item>
///   <item>Si existe activo, llamar <c>MarkSupersededAsync</c>; si eso falla
///   (e.g. race con otra request), devolver
///   <c>conflict.risk_profile.concurrent_supersede</c>.</item>
///   <item>Construir nuevo <see cref="RiskProfile"/> via factory y llamar
///   <c>AddAsync</c>.</item>
///   <item>Llamar <c>SaveChangesAsync</c> una sola vez: supersede + add
///   en la misma UoW (atomicidad al nivel del DbContext + transaccion EF).</item>
/// </list>
///
/// Loggeado solo al nivel info con el userId — el handler NO loggea
/// capital/currency/percent (PII scrubber cubre el resto; este handler
/// cumple con la regla del spec "no capital/currency/percentage at info").
/// </summary>
public sealed class CreateOrSupersedeRiskProfileHandler
    : IRequestHandler<CreateOrSupersedeRiskProfileCommand, Result<Guid>>
{
    private readonly IRiskProfileRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ILogger<CreateOrSupersedeRiskProfileHandler> _logger;

    public CreateOrSupersedeRiskProfileHandler(
        IRiskProfileRepository repo,
        IUnitOfWork uow,
        IClock clock,
        ILogger<CreateOrSupersedeRiskProfileHandler> logger)
    {
        _repo = repo;
        _uow = uow;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<Guid>> Handle(
        CreateOrSupersedeRiskProfileCommand req,
        CancellationToken ct)
    {
        var userId = req.UserId;
        if (userId == Guid.Empty)
            return Result.Failure<Guid>(Identity.Domain.Common.IdentityDomainErrors.User.IdRequired);

        // 1. Capital sanity check (positive + non-zero) — done before VO construction
        //    so that capital_amount_invalid is the early-exit code regardless of
        //    which VO would later fail.
        if (req.CapitalAmount <= 0)
            return Result.Failure<Guid>(RiskProfileErrors.CapitalOutOfRange);

        // 2. Currency via Money — Create enforces 3-letter ISO format + supported list.
        var currencyResult = Currency.Create(req.CapitalCurrency);
        if (currencyResult.IsFailure)
            return Result.Failure<Guid>(currencyResult.Error);
        var capitalResult = Money.Create(req.CapitalAmount, currencyResult.Value);
        if (capitalResult.IsFailure)
            return Result.Failure<Guid>(capitalResult.Error);

        // 3. VOs (each enforces its own range; failure carries the right code).
        var drawdownResult = MaxDrawdownPercent.Create(req.MaxDrawdownPercent);
        if (drawdownResult.IsFailure) return Result.Failure<Guid>(drawdownResult.Error);

        var riskResult = RiskPerTradePercent.Create(req.RiskPerTradePercent);
        if (riskResult.IsFailure) return Result.Failure<Guid>(riskResult.Error);

        var rrResult = RiskRewardRatio.Create(req.RiskRewardTarget);
        if (rrResult.IsFailure) return Result.Failure<Guid>(rrResult.Error);

        // 4. Look up the existing active (may be null).
        var existing = await _repo.GetActiveAsync(userId, ct);

        if (existing is not null)
        {
            // 5. Mark superseded in the DB. If this returns failure, abort without
            //    persisting — the caller will see a 409 conflict.
            var supersedeResult = await _repo.MarkSupersededAsync(existing.Id, _clock, ct);
            if (supersedeResult.IsFailure)
            {
                _logger.LogWarning("Concurrent supersede rejected for {UserId}.", userId);
                return Result.Failure<Guid>(supersedeResult.Error);
            }
        }

        // 6. Build the new active profile (always IsActive=true, SupersededAt=null).
        var profileResult = RiskProfile.Create(
            Guid.NewGuid(), userId,
            capitalResult.Value,
            drawdownResult.Value,
            riskResult.Value,
            rrResult.Value,
            _clock);
        if (profileResult.IsFailure) return Result.Failure<Guid>(profileResult.Error);

        await _repo.AddAsync(profileResult.Value, ct);

        // 7. Single SaveChangesAsync — the EF UoW commits both the supersede
        //    (Update) and the add (Insert) in one DB transaction. The DB-level
        //    UNIQUE INDEX PARTIAL on is_active remains the last-line guard.
        var saved = await _uow.SaveChangesAsync(ct);
        if (saved.IsFailure)
            return Result.Failure<Guid>(saved.Error);

        _logger.LogInformation("Risk profile upserted for {UserId}.", userId);

        return Result.Success(profileResult.Value.Id);
    }
}
