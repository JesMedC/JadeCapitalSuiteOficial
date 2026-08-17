using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.PositionSize;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JadeCapital.Trading.Application.Features.PositionSize;

// ============================================================================
//  CalculatePositionSizeHandler — slice 1b.
//
//  Pasos:
//    1. Validar Currency del request contra la capitalCurrency del perfil.
//       Si no matchean → 422 validation.position_size.currency_mismatch.
//       Esto es semantico: la division riskAmount / stopLossDistance requiere
//       que ambos esten en la misma moneda (no podemos dividir USD por EUR).
//    2. Leer el perfil activo del usuario via IIdentityUserRiskProfileReader
//       (proyeccion cross-module de slice 1a.1b). Si es null → 404
//       notfound.risk_profile.no_active_profile.
//    3. Construir Money.FromTrusted(capital, currency) y delegar al
//       PositionSizeCalculator (pure function).
//    4. Mapear PositionSizeCalculation → PositionSizeDto.
//
//  NO persiste nada. NO incrementa audit (el spec marca el calculator como
//  "informational" — el trader puede override manual y el volume calculado
//  nunca se enforce en server-side). Solo log debug con userId + amount.
//
//  Logging: PII-conscious. Solo loggea userId y riskAmount (capitalAmount
//  y percentages NO se loggean al info level).
// ============================================================================

public sealed class CalculatePositionSizeHandler
    : IRequestHandler<CalculatePositionSizeQuery, Result<PositionSizeDto>>
{
    private readonly IIdentityUserRiskProfileReader _riskProfileReader;
    private readonly ILogger<CalculatePositionSizeHandler> _logger;

    public CalculatePositionSizeHandler(
        IIdentityUserRiskProfileReader riskProfileReader,
        ILogger<CalculatePositionSizeHandler> logger)
    {
        _riskProfileReader = riskProfileReader;
        _logger = logger;
    }

    public async Task<Result<PositionSizeDto>> Handle(
        CalculatePositionSizeQuery query,
        CancellationToken ct)
    {
        // ===== Step 1: profile lookup =====
        // Cross-module projection (slice 1a.1b). El reader solo expone las
        // cuatro props que el calculator necesita; no filtra otros datos del
        // perfil (MaxDrawdownPercent, superseded state, timestamps) ni permite
        // mutate.
        var profile = await _riskProfileReader.GetActiveAsync(query.UserId, ct);
        if (profile is null)
        {
            // Spec scenario "No active risk profile" → 404.
            return Result.Failure<PositionSizeDto>(
                Error.NotFound(
                    "risk_profile.no_active_profile",
                    "No active risk profile. Configure one before calculating position size."));
        }

        // ===== Step 2: currency match =====
        // El request.currency es la moneda del stopLossDistance (quote del
        // symbol). La capitalCurrency del perfil es donde vive el riesgo. Si
        // difieren, no podemos hacer la division sin un FX rate — el spec no
        // incluye conversion automatica, asi que rechazamos con 422.
        if (!string.Equals(query.Currency, profile.CapitalCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<PositionSizeDto>(PositionSizeErrors.CurrencyMismatch);
        }

        // ===== Step 3: run calculator =====
        var capital = Money.FromTrusted(profile.CapitalAmount, profile.CapitalCurrency);

        var calculationResult = PositionSizeCalculator.Calculate(
            capital: capital,
            riskPerTradePercent: profile.RiskPerTradePercent,
            stopLossDistance: query.StopLossDistance,
            riskOverridePercent: query.RiskPerTradeOverride);

        if (calculationResult.IsFailure)
        {
            // Propaga los validation.position_size.* errors del calculator
            // (invalid_stop_loss, invalid_capital, invalid_risk_percent).
            return Result.Failure<PositionSizeDto>(calculationResult.Error);
        }

        var calc = calculationResult.Value;

        _logger.LogDebug(
            "Position-size calculated for user {UserId}: riskAmount={RiskAmount} {Currency}.",
            query.UserId,
            calc.RiskAmount,
            profile.CapitalCurrency);

        // ===== Step 4: map to DTO =====
        var dto = new PositionSizeDto(
            Volume: calc.Volume,
            RiskAmount: calc.RiskAmount,
            RiskPerTradePercent: calc.RiskPerTradePercent,
            Currency: query.Currency,
            Calculation: calc.Calculation,
            RecommendedStopLossDistance: null);

        return Result.Success(dto);
    }
}
