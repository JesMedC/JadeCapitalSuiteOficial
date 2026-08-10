using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.UnitTests.Application.Instruments;

public class GetInstrumentsHandlerTests
{
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();

    private GetInstrumentsHandler CreateSut() => new(_instruments);

    private static Instrument CreateInstrument(string symbol)
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Instrument.Create(
            Guid.NewGuid(), symbol, AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, c).Value;
    }

    [Fact]
    public async Task Handle_ActiveOnly_CallsListActive()
    {
        var list = new List<Instrument>
        {
            CreateInstrument("EUR/USD"),
            CreateInstrument("GBP/USD")
        };
        _instruments.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(list);

        var result = await CreateSut().Handle(new GetInstrumentsQuery(ActiveOnly: true), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        await _instruments.Received(1).ListActiveAsync(Arg.Any<CancellationToken>());
        await _instruments.DidNotReceive().ListAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActiveOnlyFalse_CallsListAll()
    {
        var list = new List<Instrument>
        {
            CreateInstrument("EUR/USD"),
            CreateInstrument("GBP/USD")
        };
        _instruments.ListAllAsync(Arg.Any<CancellationToken>()).Returns(list);

        var result = await CreateSut().Handle(new GetInstrumentsQuery(ActiveOnly: false), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        await _instruments.Received(1).ListAllAsync(Arg.Any<CancellationToken>());
        await _instruments.DidNotReceive().ListActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyList_ReturnsEmpty()
    {
        _instruments.ListActiveAsync(Arg.Any<CancellationToken>()).Returns(new List<Instrument>());

        var result = await CreateSut().Handle(new GetInstrumentsQuery(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
