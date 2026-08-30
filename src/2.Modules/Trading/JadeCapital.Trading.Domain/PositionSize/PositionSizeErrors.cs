using JadeCapital.Shared.Kernel.Results;

namespace JadeCapital.Trading.Domain.PositionSize;

// ============================================================================
//  PositionSizeErrors — slice 1b.
//
//  Codigos SIN el prefijo "validation." — el factory Error.Validation ya lo
//  agrega, igual que en TradingDomainErrors. Los codigos finales quedan:
//    - validation.position_size.invalid_stop_loss    (HTTP 422)
//    - validation.position_size.invalid_capital      (HTTP 422)
//    - validation.position_size.invalid_risk_percent (HTTP 422)
//    - validation.position_size.currency_mismatch    (HTTP 422)
//
//  Todos caen en la categoria validation.* que ProblemFromResult mapea a 422
//  (semantica: el request esta bien formado, pero la regla de negocio falla).
// ============================================================================

public static class PositionSizeErrors
{
    public static readonly Error InvalidStopLoss =
        Error.Validation(
            "position_size.invalid_stop_loss",
            "Stop loss distance must be greater than zero.");

    public static readonly Error InvalidCapital =
        Error.Validation(
            "position_size.invalid_capital",
            "Risk amount must be greater than zero (capital must be positive).");

    public static readonly Error InvalidRiskPercent =
        Error.Validation(
            "position_size.invalid_risk_percent",
            "Effective risk per trade percent must be greater than zero.");

    public static readonly Error CurrencyMismatch =
        Error.Validation(
            "position_size.currency_mismatch",
            "Stop loss currency does not match the user's risk profile capital currency.");
}
