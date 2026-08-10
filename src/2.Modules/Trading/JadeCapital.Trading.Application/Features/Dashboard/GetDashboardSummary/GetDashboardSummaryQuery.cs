using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Dashboard.GetDashboardSummary;

public sealed record GetDashboardSummaryQuery(
    Guid UserId,
    DateTimeOffset From,
    DateTimeOffset To) : IRequest<Result<DashboardSummaryDto>>;

public sealed class GetDashboardSummaryValidator : AbstractValidator<GetDashboardSummaryQuery>
{
    public GetDashboardSummaryValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.From).LessThan(x => x.To);
    }
}