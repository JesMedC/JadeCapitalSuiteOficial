using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Shared.Kernel.Results;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Identity.Application.Features.RiskProfileActions.GetActiveRiskProfile;

/// <summary>
/// MediatR handler para <see cref="GetActiveRiskProfileQuery"/>. Proyecta el
/// aggregate activo a <see cref="RiskProfileDto"/>; si no hay perfil activo,
/// devuelve <c>notfound.risk_profile.not_found</c> (mapeado a 404 por
/// ProblemFromResult en el endpoint).
///
/// Loggeado solo al nivel debug con el userId — el handler NO loggea
/// capital/currency/percentage al nivel info (regla del spec "no PII").
/// </summary>
public sealed class GetActiveRiskProfileHandler
    : IRequestHandler<GetActiveRiskProfileQuery, Result<RiskProfileDto>>
{
    private readonly IRiskProfileRepository _repo;
    private readonly ILogger<GetActiveRiskProfileHandler> _logger;

    public GetActiveRiskProfileHandler(
        IRiskProfileRepository repo,
        ILogger<GetActiveRiskProfileHandler> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<Result<RiskProfileDto>> Handle(
        GetActiveRiskProfileQuery req,
        CancellationToken ct)
    {
        var profile = await _repo.GetActiveAsync(req.UserId, ct);
        if (profile is null)
        {
            _logger.LogDebug("No active risk profile for {UserId}.", req.UserId);
            return Result.Failure<RiskProfileDto>(RiskProfileErrors.NotFound);
        }

        return Result.Success(new RiskProfileDto(
            profile.Id,
            profile.UserId,
            profile.CapitalAmount,
            profile.CapitalCurrencyCode,
            profile.MaxDrawdownPercent.Value,
            profile.RiskPerTradePercent.Value,
            profile.RiskRewardTarget.Value,
            profile.IsActive,
            profile.SupersededAt,
            profile.CreatedAt,
            profile.UpdatedAt ?? profile.CreatedAt));
    }
}
