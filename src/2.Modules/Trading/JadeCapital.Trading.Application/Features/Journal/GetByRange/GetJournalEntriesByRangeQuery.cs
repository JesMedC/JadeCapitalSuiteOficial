using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using JadeCapital.Trading.Contracts.Journal;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Journal.GetByRange;

/// <summary>
/// Query: lista los journal entries del usuario en un rango de fechas
/// locales (inclusivo en ambos extremos) — <c>GET /api/journal?from=YYYY-MM-DD&amp;to=YYYY-MM-DD</c>.
///
/// El FE lo usa para el calendario mensual o el grafico de PnL
/// diario cruzado con journal. El limite maximo de rango lo enforce
/// el endpoint (e.g. 90 dias) — esta query no tiene cap interno.
/// </summary>
public sealed record GetJournalEntriesByRangeQuery(
    Guid UserId,
    LocalDate From,
    LocalDate To) : IRequest<Result<IReadOnlyList<JournalEntryDto>>>;

/// <summary>
/// List handler. Pasa el rango al repositorio (que filtra por userId
/// para cross-user scope) y mapea cada entry a DTO.
///
/// Si <c>From &gt; To</c>, devuelve una lista vacia (no es un error
/// — el FE podria invertir el orden por accidente y queremos
/// degradar graciosamente).
/// </summary>
public sealed class GetJournalEntriesByRangeHandler
    : IRequestHandler<GetJournalEntriesByRangeQuery, Result<IReadOnlyList<JournalEntryDto>>>
{
    private readonly IJournalEntryRepository _entries;

    public GetJournalEntriesByRangeHandler(IJournalEntryRepository entries)
    {
        _entries = entries;
    }

    public async Task<Result<IReadOnlyList<JournalEntryDto>>> Handle(
        GetJournalEntriesByRangeQuery req,
        CancellationToken ct)
    {
        if (req.From > req.To)
            return Result.Success<IReadOnlyList<JournalEntryDto>>(Array.Empty<JournalEntryDto>());

        var entries = await _entries.ListByRangeAsync(req.UserId, req.From, req.To, ct);
        var dtos = entries.Select(e => e.ToDto()).ToList();
        return Result.Success<IReadOnlyList<JournalEntryDto>>(dtos);
    }
}
