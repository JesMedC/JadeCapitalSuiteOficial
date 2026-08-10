using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.GetInstrumentById;

/// <summary>
/// Trae un instrumento por id. Sin ownership (es global): cualquier usuario
/// autenticado puede ver cualquier instrumento.
/// </summary>
public sealed record GetInstrumentByIdQuery(
    Guid InstrumentId) : IRequest<Result<InstrumentDto>>;
