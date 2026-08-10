using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Application._Common;
using MediatR;

namespace JadeCapital.Trading.Application.Features.Instruments.GetInstrumentById;

public sealed class GetInstrumentByIdHandler : IRequestHandler<GetInstrumentByIdQuery, Result<InstrumentDto>>
{
    private readonly IInstrumentRepository _instruments;

    public GetInstrumentByIdHandler(IInstrumentRepository instruments)
    {
        _instruments = instruments;
    }

    public async Task<Result<InstrumentDto>> Handle(GetInstrumentByIdQuery req, CancellationToken ct)
    {
        var instrument = await _instruments.FindByIdAsync(req.InstrumentId, ct);
        if (instrument is null)
            return Result.Failure<InstrumentDto>(TradingApplicationErrors.Instruments.NotFound);

        return Result.Success(instrument.ToDto());
    }
}
