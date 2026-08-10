using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.UnitTests.Application.Instruments;

public class GetInstrumentByIdHandlerTests
{
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();

    private GetInstrumentByIdHandler CreateSut() => new(_instruments);

    private static Instrument CreateInstrument(string symbol)
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Instrument.Create(
            Guid.NewGuid(), symbol, AssetClass.Forex,
            100000m, 5, 0.0001m, 0.85m, c).Value;
    }

    [Fact]
    public async Task Handle_ExistingInstrument_ReturnsDto()
    {
        var instrument = CreateInstrument("EUR/USD");
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);

        var result = await CreateSut().Handle(new GetInstrumentByIdQuery(instrument.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(instrument.Id);
        result.Value.Symbol.Should().Be("EUR/USD");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        _instruments.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(new GetInstrumentByIdQuery(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.instrument");
    }
}
