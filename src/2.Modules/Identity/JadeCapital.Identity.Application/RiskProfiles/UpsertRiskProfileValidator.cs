using FluentValidation;
using JadeCapital.Identity.Contracts.RiskProfiles;
using JadeCapital.Identity.Domain.RiskProfile;

namespace JadeCapital.Identity.Application.RiskProfiles;

/// <summary>
/// FluentValidation del body de <c>PUT /api/risk-profile</c>. Re-chekea
/// los rangos del spec a la entrada HTTP antes de invocar el handler.
///
/// Defense in depth: la Validacion corre aca (capa API) y el aggregate
/// (capa dominio) la vuelve a correr via los VOs. Si este check cambia
/// accidentalmente (e.g. alguien sube el techo del risk-per-trade de 5%
/// a 10%), el aggregate y la DB CHECK siguen enforcing los limites
/// originales — el sistema se rompe ruidosamente en lugar de quedar
/// permisivo.
/// </summary>
public sealed class UpsertRiskProfileValidator : AbstractValidator<UpsertRiskProfileRequest>
{
    public UpsertRiskProfileValidator()
    {
        RuleFor(x => x.CapitalAmount)
            .GreaterThan(0m)
                .WithMessage(RiskProfileErrors.CapitalOutOfRange.Message);

        RuleFor(x => x.CapitalCurrency)
            .NotEmpty().WithMessage("Capital currency is required.")
            .Length(3).WithMessage("Capital currency must be exactly 3 characters.")
            .Matches("^[A-Za-z]{3}$").WithMessage("Capital currency must be 3 letters.");

        RuleFor(x => x.MaxDrawdownPercent)
            .InclusiveBetween(MaxDrawdownPercent.MinValue, MaxDrawdownPercent.MaxValue)
                .WithMessage($"Max drawdown percent must be between {MaxDrawdownPercent.MinValue:0.00} and {MaxDrawdownPercent.MaxValue:0.00}.");

        RuleFor(x => x.RiskPerTradePercent)
            .InclusiveBetween(RiskPerTradePercent.MinValue, RiskPerTradePercent.MaxValue)
                .WithMessage($"Risk-per-trade percent must be between {RiskPerTradePercent.MinValue:0.00} and {RiskPerTradePercent.MaxValue:0.00}.");

        RuleFor(x => x.RiskRewardTarget)
            .GreaterThanOrEqualTo(RiskRewardRatio.MinValue)
                .WithMessage($"Risk-reward target must be greater than or equal to {RiskRewardRatio.MinValue:0.0}.");
    }
}
