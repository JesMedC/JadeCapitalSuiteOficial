using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.GetInstruments;

/// <summary>
/// Lista instrumentos. Instrument es global, sin ownership. El flag
/// ActiveOnly=true filtra a IsActive=true (default en UI operativa);
/// ActiveOnly=false devuelve todos (admin / debugging).
/// </summary>
public sealed record GetInstrumentsQuery(
    bool ActiveOnly = true) : IRequest<Result<IReadOnlyList<InstrumentDto>>>;
