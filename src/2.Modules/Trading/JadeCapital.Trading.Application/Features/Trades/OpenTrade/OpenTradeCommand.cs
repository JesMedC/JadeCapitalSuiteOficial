using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Enums;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Trades.OpenTrade;

/// <summary>
/// DTO del checklist pre-trade que llega en el body de
/// <c>POST /api/trades</c>. Vive en Application (no en Contracts)
/// porque es contrato de la capa de aplicacion — el endpoint API
/// solo lo reenvia, y el handler lo traduce al VO inmutable
/// <see cref="PreTradeChecklistSubmission"/>.
///
/// <see cref="RiskRewardTargetUsed"/> es opcional: cuando el caller
/// envia null/0, el handler resuelve el target desde el perfil activo
/// (vía <c>IIdentityUserRiskProfileReader</c>) o cae al default 1.0 si
/// no hay perfil. Ver spec scenario "No active risk profile".
/// </summary>
public sealed record PreTradeChecklistSubmissionInput(
    Emotionality Emotionality,
    SetupQuality SetupQuality,
    decimal RiskRewardAtEntry,
    decimal RiskRewardTargetUsed,
    byte ConfluencesCount);

public sealed record OpenTradeCommand(
    Guid UserId,
    Guid AccountId,
    Guid InstrumentId,
    string Symbol,
    AssetClass AssetClass,
    TradeDirection Direction,
    decimal Volume,
    string VolumeCurrency,
    decimal EntryPrice,
    string EntryPriceCurrency,
    string? Strategy,
    string? Notes,
    PreTradeChecklistSubmissionInput? Checklist = null) : IRequest<Result<TradeDto>>;

public sealed class OpenTradeValidator : AbstractValidator<OpenTradeCommand>
{
    public OpenTradeValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.AccountId).NotEqual(Guid.Empty);
        RuleFor(x => x.InstrumentId).NotEqual(Guid.Empty);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Volume).GreaterThan(0);
        RuleFor(x => x.VolumeCurrency).NotEmpty().Length(3);
        RuleFor(x => x.EntryPrice).GreaterThan(0);
        RuleFor(x => x.EntryPriceCurrency).NotEmpty().Length(3);
        RuleFor(x => x.Strategy).MaximumLength(80);
        RuleFor(x => x.Notes).MaximumLength(2000);

        // Checklist fields (opcional — solo se valida si viene).
        When(x => x.Checklist is not null, () =>
        {
            RuleFor(x => x.Checklist!.Emotionality).IsInEnum();
            RuleFor(x => x.Checklist!.SetupQuality).IsInEnum();
            RuleFor(x => x.Checklist!.RiskRewardAtEntry).GreaterThanOrEqualTo(1m);
            RuleFor(x => x.Checklist!.RiskRewardTargetUsed).GreaterThanOrEqualTo(0m);
            RuleFor(x => x.Checklist!.ConfluencesCount).InclusiveBetween((byte)1, (byte)10);
        });
    }
}
