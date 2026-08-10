using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.GetInstruments;

/// <summary>
/// Lista instrumentos segun el flag ActiveOnly. Instrument es global, sin
/// ownership: cualquier usuario autenticado puede ver el catalogo.
/// </summary>
public sealed class GetInstrumentsHandler : IRequestHandler<GetInstrumentsQuery, Result<IReadOnlyList<InstrumentDto>>>
{
    private readonly IInstrumentRepository _instruments;

    public GetInstrumentsHandler(IInstrumentRepository instruments)
    {
        _instruments = instruments;
    }

    public async Task<Result<IReadOnlyList<InstrumentDto>>> Handle(GetInstrumentsQuery req, CancellationToken ct)
    {
        var items = req.ActiveOnly
            ? await _instruments.ListActiveAsync(ct)
            : await _instruments.ListAllAsync(ct);

        return Result.Success<IReadOnlyList<InstrumentDto>>(items.Select(i => i.ToDto()).ToList());
    }
}
