using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Domain.PositionSize;
using MediatR;

namespace JadeCapital.Trading.Application.Features.PositionSize;

// ============================================================================
//  CalculatePositionSize — slice 1b.
//
//  Query + DTO + validator para `POST /api/trades/position-size/calculate`.
//  El handler orquestacion esta en `CalculatePositionSizeHandler.cs` (archivo
//  separado por consistencia con el resto de slices — OpenTradeCommand + Handler,
//  GetTradingMetricsQuery + Handler, etc.).
//
//  Forma del request body:
//    POST /api/trades/position-size/calculate
//    {
//      "stopLossDistance": 0.0050,
//      "riskPerTradeOverride": 2.0,   // opcional
//      "currency": "USD"
//    }
//
//  El handler NO requiere un `instrumentId` en esta primera version: la
//  conversion a unidades del instrumento se hara en una iteracion futura que
//  resolvera `IInstrumentRepository` para redondear el `volume` a
//  `instrument.decimalPlaces`. Por ahora el calculator entrega unidades base
//  raw (decimal) y el FE las redondea segun el symbol seleccionado.
//
//  El DTO expone:
//    - Volume                        : unidades base (sin redondeo).
//    - RiskAmount                    : capital × risk% / 100.
//    - RiskPerTradePercent           : efectivo (override ?? profile).
//    - Currency                      : quote currency del request (echo del caller).
//    - Calculation                   : "(C × p% / 100) / |stop| = volume" —
//                                      explicacion textual para logs/UI.
//    - RecommendedStopLossDistance?  : placeholder para una iteracion futura
//                                      donde el caller podria pedir una
//                                      sugerencia sin proveer stop. Siempre
//                                      null en este slice (el request requiere
//                                      stopLossDistance).
// ============================================================================

public sealed record PositionSizeRequest(
    decimal StopLossDistance,
    decimal? RiskPerTradeOverride,
    string Currency);

public sealed record PositionSizeDto(
    decimal Volume,
    decimal RiskAmount,
    decimal RiskPerTradePercent,
    string Currency,
    string Calculation,
    decimal? RecommendedStopLossDistance);

public sealed record CalculatePositionSizeQuery(
    Guid UserId,
    decimal StopLossDistance,
    decimal? RiskPerTradeOverride,
    string Currency) : IRequest<Result<PositionSizeDto>>;

/// <summary>
/// Validator de FluentValidation. Sigue el patron de OpenTradeValidator.
/// NOTA: el assembly de Trading.Application NO esta registrado en DI hoy
/// (solo Identity.Application se registra en Program.cs via
/// <c>AddAssemblyValidators(typeof(RegisterUserValidator).Assembly)</c>),
/// asi que este validator es defensivo mas que un enforcement automatico.
/// El handler re-valida los rangos criticos de todas formas.
/// </summary>
public sealed class CalculatePositionSizeValidator
    : AbstractValidator<CalculatePositionSizeQuery>
{
    public CalculatePositionSizeValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.StopLossDistance).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        When(x => x.RiskPerTradeOverride.HasValue, () =>
        {
            // Spec scenario "Out-of-range override" → 422.
            RuleFor(x => x.RiskPerTradeOverride!.Value)
                .InclusiveBetween(0.01m, 5.00m);
        });
    }
}
