using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.UnitTests.Application.Instruments;

public class DeleteInstrumentHandlerTests
{
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<DeleteInstrumentHandler> _logger = Substitute.For<ILogger<DeleteInstrumentHandler>>();

    private DeleteInstrumentHandler CreateSut() => new(_instruments, _trades, _uow, _logger);

    private static Instrument CreateInstrument(string symbol)
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Instrument.Create(
            Guid.NewGuid(), symbol, AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, c).Value;
    }

    [Fact]
    public async Task Handle_NoTrades_DeletesInstrument()
    {
        var instrument = CreateInstrument("EUR/USD");
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);
        _trades.CountByInstrumentIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(0);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await CreateSut().Handle(new DeleteInstrumentCommand(instrument.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _instruments.Received(1).RemoveAsync(instrument, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HasTrades_ReturnsConflict()
    {
        var instrument = CreateInstrument("EUR/USD");
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);
        _trades.CountByInstrumentIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(5);

        var result = await CreateSut().Handle(new DeleteInstrumentCommand(instrument.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.instrument.has_trades");
        await _instruments.DidNotReceive().RemoveAsync(Arg.Any<Instrument>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        _instruments.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(new DeleteInstrumentCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.instrument");
    }
}
