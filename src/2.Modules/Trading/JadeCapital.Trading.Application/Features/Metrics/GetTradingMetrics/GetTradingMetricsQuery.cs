using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Domain.Metrics;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Metrics.GetTradingMetrics;

/// <summary>
/// Query para /api/trades/metrics?period=... Resuelve las metricas del usuario
/// autenticado en la ventana temporal solicitada.
///
/// El handler:
///   1. Verifica que el user exista (probe — placeholder en slice 1f).
///   2. Lee trades y conteos del IMetricsQueryStore.
///   3. Pasa todo al MetricsCalculator (pure, domain).
///   4. Proyecta el MetricsResult a MetricsDto.
///
/// El currency del DTO es siempre USD por ahora (v1 — los traders operan en
/// una sola moneda quotable); se proyecta a Money en la respuesta si el
/// FE lo pide.
/// </summary>
public sealed record GetTradingMetricsQuery(
    Guid UserId,
    MetricsPeriod Period) : IRequest<Result<MetricsDto>>;

public sealed class GetTradingMetricsValidator : AbstractValidator<GetTradingMetricsQuery>
{
    public GetTradingMetricsValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Period).IsInEnum();
    }
}
