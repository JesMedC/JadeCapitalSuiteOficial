using FluentAssertions;
using FluentValidation.TestHelper;
using JadeCapital.Trading.Application.Features.Strategies.CreateStrategy;
using JadeCapital.Trading.Domain.Strategies;

namespace JadeCapital.Trading.UnitTests.Strategies;

// ============================================================================
//  CreateStrategyHandlerTests — slice 3a.
//
//  Four scenarios called out in the work-unit breakdown:
//   1. Valid create persists the aggregate + returns DTO.
//   2. Duplicate active name → 409 (strategy.duplicate_name).
//   3. Validation errors propagate from Strategy.Create (name too long).
//   4. UserId empty fails validation.
// ============================================================================

public class CreateStrategyHandlerTests
{
    private readonly IStrategyRepository _repo = Substitute.For<IStrategyRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public CreateStrategyHandlerTests()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 18, 14, 0, 0, TimeSpan.Zero));
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result<int>.Success(1));
    }

    private CreateStrategyHandler CreateSut() => new(_repo, _uow, _clock);

    private static CreateStrategyCommand ValidCmd() => new(
        UserId: Guid.NewGuid(),
        Name: "London Break",
        Description: "London session breakout",
        Symbol: "EUR/USD",
        Timeframe: (byte)Timeframe.H1,
        Rules: "Enter on 1H close above range high");

    [Fact]
    public async Task Handle_ValidArgs_PersistsAggregateAndReturnsDto()
    {
        var cmd = ValidCmd();
        _repo.ExistsByNameAsync(cmd.UserId, cmd.Name, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be(cmd.Name);
        result.Value.UserId.Should().Be(cmd.UserId);
        result.Value.IsActive.Should().BeTrue();
        result.Value.Timeframe.Should().Be((byte)Timeframe.H1);

        await _repo.Received(1).AddAsync(
            Arg.Is<Strategy>(s => s.UserId == cmd.UserId && s.Name == cmd.Name),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateActiveName_ReturnsConflict()
    {
        var cmd = ValidCmd();
        _repo.ExistsByNameAsync(cmd.UserId, cmd.Name, Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.strategy.duplicate_name");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Strategy>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NameOver64Chars_PropagatesValidationError()
    {
        var cmd = ValidCmd() with { Name = new string('x', 65) };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.strategy.name_too_long");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Strategy>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyUserId_FailsViaValidator()
    {
        var cmd = ValidCmd() with { UserId = Guid.Empty };
        var validator = new CreateStrategyValidator();

        var result = await validator.TestValidateAsync(cmd);

        result.IsValid.Should().BeFalse();
        result.ShouldHaveValidationErrorFor(c => c.UserId);
    }
}