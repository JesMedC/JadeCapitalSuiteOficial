using NSubstitute.ReturnsExtensions;
using JadeCapital.Trading.Domain.Instruments;

namespace JadeCapital.Trading.UnitTests.Application.Instruments;

public class DeactivateInstrumentHandlerTests
{
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ILogger<DeactivateInstrumentHandler> _logger = Substitute.For<ILogger<DeactivateInstrumentHandler>>();

    private DeactivateInstrumentHandler CreateSut() => new(_instruments, _uow, _logger);

    private static Instrument CreateActiveInstrument()
    {
        var c = Substitute.For<IClock>();
        c.UtcNow.Returns(new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero));
        return Instrument.Create(
            Guid.NewGuid(), "EUR/USD", AssetClass.Forex | AssetClass.Binary,
            100000m, 5, 0.0001m, 0.85m, c).Value;
    }

    [Fact]
    public async Task Handle_ActiveInstrument_DeactivatesAndReturnsDto()
    {
        var instrument = CreateActiveInstrument();
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var result = await CreateSut().Handle(new DeactivateInstrumentCommand(instrument.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsActive.Should().BeFalse();
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyInactive_ReturnsConflict()
    {
        var instrument = CreateActiveInstrument();
        instrument.Deactivate();
        _instruments.FindByIdAsync(instrument.Id, Arg.Any<CancellationToken>()).Returns(instrument);

        var result = await CreateSut().Handle(new DeactivateInstrumentCommand(instrument.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.instrument.already_inactive");
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsNotFound()
    {
        _instruments.FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().Handle(new DeactivateInstrumentCommand(Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("notfound.instrument");
    }
}
