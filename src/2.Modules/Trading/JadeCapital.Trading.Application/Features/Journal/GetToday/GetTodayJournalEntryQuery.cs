using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Journal;
using JadeCapital.Trading.Domain.Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Journal.GetToday;

/// <summary>
/// Query: devuelve el journal entry del usuario para la fecha local
/// de "hoy" en su timezone (<c>GET /api/journal/today</c>).
///
/// "Hoy" se computa a partir de <see cref="IClock.UtcNow"/> y el
/// <c>Timezone</c> IANA que el cliente envio. Si no hay entry, devuelve
/// 404 con <c>notfound.journal.not_found</c>.
/// </summary>
public sealed record GetTodayJournalEntryQuery(
    Guid UserId,
    string Timezone) : IRequest<Result<JournalEntryDto>>;

/// <summary>
/// Resuelve today = LocalDate.From(clock.UtcNow, timezone), luego hace
/// GetByUserAndDateAsync(userId, today). Cross-user scope: la query
/// filtra por userId en el repositorio (la firma del repo lo enforce).
/// </summary>
public sealed class GetTodayJournalEntryHandler
    : IRequestHandler<GetTodayJournalEntryQuery, Result<JournalEntryDto>>
{
    private readonly IJournalEntryRepository _entries;
    private readonly IClock _clock;

    public GetTodayJournalEntryHandler(IJournalEntryRepository entries, IClock clock)
    {
        _entries = entries;
        _clock = clock;
    }

    public async Task<Result<JournalEntryDto>> Handle(
        GetTodayJournalEntryQuery req,
        CancellationToken ct)
    {
        var today = LocalDate.From(_clock.UtcNow, req.Timezone);
        var entry = await _entries.GetByUserAndDateAsync(req.UserId, today, ct);
        if (entry is null)
            return Result.Failure<JournalEntryDto>(TradingDomainErrors.Journal.NotFound);

        return Result.Success(entry.ToDto());
    }
}
