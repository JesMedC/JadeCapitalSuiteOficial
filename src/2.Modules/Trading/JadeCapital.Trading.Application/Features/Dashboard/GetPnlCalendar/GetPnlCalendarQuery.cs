using FluentValidation;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Dashboard.GetPnlCalendar;

public sealed record GetPnlCalendarQuery(
    Guid UserId,
    int Year,
    int Month) : IRequest<Result<CalendarDto>>;

public sealed class GetPnlCalendarValidator : AbstractValidator<GetPnlCalendarQuery>
{
    public GetPnlCalendarValidator()
    {
        RuleFor(x => x.UserId).NotEqual(Guid.Empty);
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}