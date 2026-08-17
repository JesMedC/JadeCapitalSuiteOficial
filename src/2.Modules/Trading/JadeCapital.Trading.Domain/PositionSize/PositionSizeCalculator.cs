using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Domain.PositionSize;

// ============================================================================
//  PositionSizeCalculator — slice 1b.
//
//  Static pure-function calculator: (Money capital, decimal riskPerTradePercent,
//  decimal stopLossDistance, decimal? riskOverridePercent = null) →
//  Result<PositionSizeCalculation>.
//
//  Formula (spec scenario "Valid inputs"):
//      riskAmount = capital × effectiveRiskPercent / 100
//      volume     = riskAmount / |stopLossDistance|
//
//  effectiveRiskPercent = riskOverridePercent ?? riskPerTradePercent.
//
//  Decisiones de diseno:
//
//  1. La firma toma `Money capital` (VO) y `decimal stopLossDistance` (raw).
//     Esto es deliberado: la conversion a unidades del instrumento depende
//     de metadata (contractSize, pipValue, decimalPlaces) que el calculator
//     no carga — el handler o un future slice la resolveran con
//     `IInstrumentRepository`. Slice 1b acepta el stopLossDistance ya en
//     unidades del quote currency (ej: EUR/USD pip = 0.0001) y devuelve
//     volume en unidades base, sin redondeo.
//
//  2. NO clampeamos riskAmount a capitalAmount: el validator de FluentValidation
//     asegura riskPerTradePercent en [0.01, 5.00], asi que riskAmount sera
//     siempre <= 0.05 × capital en el happy path. Clamping extra iria contra
//     el principio "calculadora informational" del spec.
//
//  3. NO usamos `decimal.Abs` para stopLossDistance: si llega negativo lo
//     rechazamos. La API deberia garantizar positivo (spec scenario "Stop
//     equal to entry"), pero la calculadora queda defensiva.
//
//  4. La cadena `Calculation` se construye una sola vez — es metadata de UI/
//     log, no parte del calculo.
// ============================================================================

public static class PositionSizeCalculator
{
    public static Result<PositionSizeCalculation> Calculate(
        Money capital,
        decimal riskPerTradePercent,
        decimal stopLossDistance,
        decimal? riskOverridePercent = null)
    {
        if (capital is null)
            return Result.Failure<PositionSizeCalculation>(
                PositionSizeErrors.InvalidCapital);

        // Stop must be strictly positive (spec scenario "Stop equal to entry" → 422).
        if (stopLossDistance <= 0m)
            return Result.Failure<PositionSizeCalculation>(
                PositionSizeErrors.InvalidStopLoss);

        var effectiveRiskPercent = riskOverridePercent ?? riskPerTradePercent;

        // Defensive: a non-positive risk% makes no sense (negative risk or
        // zero risk yields volume=NaN or zero). The validator already bounds
        // this to [0.01, 5.00], but we stay defensive in case a future caller
        // bypasses the validator (e.g., a background job).
        if (effectiveRiskPercent <= 0m)
            return Result.Failure<PositionSizeCalculation>(
                PositionSizeErrors.InvalidRiskPercent);

        // capital.Amount > 0 is enforced by Money.Create, but we re-check
        // for the FromTrusted path (used at hydration time).
        if (capital.Amount <= 0m)
            return Result.Failure<PositionSizeCalculation>(
                PositionSizeErrors.InvalidCapital);

        var riskAmount = capital.Amount * effectiveRiskPercent / 100m;
        var volume = riskAmount / stopLossDistance;

        var calculation =
            $"({capital.Amount} × {effectiveRiskPercent}% / 100) / {stopLossDistance} = {volume}";

        return Result.Success(new PositionSizeCalculation(
            Volume: volume,
            RiskAmount: riskAmount,
            RiskPerTradePercent: effectiveRiskPercent,
            Calculation: calculation));
    }
}
