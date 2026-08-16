namespace JadeCapital.Trading.Domain.PositionSize;

// ============================================================================
//  PositionSizeCalculation — slice 1b.
//
//  Resultado inmutable del PositionSizeCalculator. NO se persiste: el calculator
//  es read-only y por diseno nunca toca la DB (spec scenario "No active risk
//  profile" → 404; los calculos no necesitan UoW).
//
//  - Volume                  : unidades base del instrumento (puede ser fraccional,
//                              depende del contractSize / pipValue del symbol).
//                              Sin redondeo: el caller (handler) decide como
//                              presentarlo al usuario.
//  - RiskAmount              : (CapitalAmount × effectiveRiskPercent) / 100.
//                              En la misma moneda que capital.
//  - RiskPerTradePercent     : el porcentaje EFECTIVAMENTE usado (override si viene,
//                              sino el del perfil).
//  - Calculation             : explicacion textual "(C × p% / 100) / |stop| = volume".
//                              Util para logs de auditoria y para mostrar al usuario
//                              como se llego al numero.
// ============================================================================

public sealed record PositionSizeCalculation(
    decimal Volume,
    decimal RiskAmount,
    decimal RiskPerTradePercent,
    string Calculation);
