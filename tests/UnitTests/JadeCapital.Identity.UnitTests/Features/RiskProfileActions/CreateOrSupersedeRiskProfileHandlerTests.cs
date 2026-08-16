using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Identity.Application.Features.RiskProfileActions.CreateOrSupersedeRiskProfile;
using JadeCapital.Shared.Kernel.Money;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.UnitTests.Features.RiskProfileActions;

/// <summary>
/// Tests del handler <c>CreateOrSupersedeRiskProfileHandler</c> — slice 1a.1a.
///
/// Cobertura (4 escenarios del spec):
/// <list type="number">
///   <item><b>Happy path first create</b>: no existe perfil activo;
///   <c>MarkSupersededAsync</c> no se llama; <c>AddAsync</c> +
///   <c>SaveChangesAsync</c> se llaman exactamente una vez; el resultado
///   expone el id del nuevo perfil.</item>
///   <item><b>Happy path supersede</b>: existe un perfil activo; el
///   handler lo marca superseded (MarkSupersededAsync recibe id=active.Id)
///   antes de hacer AddAsync del nuevo; ambos commits del SaveChangesAsync
///   son atomicos.</item>
///   <item><b>Supersede failure (concurrent update)</b>: el repo devuelve
///   <c>Result.Failure(ConcurrentSupersede)</c>; el handler returns
///   409 sin persistir ni emitir savechanges.</item>
///   <item><b>Domain validation failure</b>: capital no positivo o
///   out-of-range; el handler returns
///   <c>validation.risk_profile.capital_amount_invalid</c> (422) sin tocar
///   el repo.</item>
/// </list>
/// </summary>
public class CreateOrSupersedeRiskProfileHandlerTests
{
    private readonly IRiskProfileRepository _repo = Substitute.For<IRiskProfileRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly ILogger<CreateOrSupersedeRiskProfileHandler> _logger = Substitute.For<ILogger<CreateOrSupersedeRiskProfileHandler>>();

    public CreateOrSupersedeRiskProfileHandlerTests()
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
    }

    private CreateOrSupersedeRiskProfileHandler CreateSut()
        => new(_repo, _uow, _clock, _logger);

    private static CreateOrSupersedeRiskProfileCommand ValidCommand(
        Guid? userId = null,
        decimal capital = 10_000m,
        string currency = "USD",
        decimal maxDrawdown = 20m,
        decimal riskPerTrade = 1m,
        decimal rrTarget = 2m)
        => new(userId ?? Guid.NewGuid(), capital, currency, maxDrawdown, riskPerTrade, rrTarget);

    [Fact]
    public async Task Handle_NoExistingActive_CreatesNewProfile_NoSupersedeCall()
    {
        _repo.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskProfile?)null);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Success(1));

        var cmd = ValidCommand();
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _repo.Received(1).AddAsync(
            Arg.Is<RiskProfile>(p => p.IsActive && p.SupersededAt == null && p.CapitalAmount == 10_000m),
            Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().MarkSupersededAsync(Arg.Any<Guid>(), Arg.Any<IClock>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingActive_SupersedesThenAddsAtomically()
    {
        var userId = Guid.NewGuid();
        var existing = RiskProfile.Create(
            Guid.NewGuid(), userId,
            Money.FromTrusted(5_000m, "USD"),
            MaxDrawdownPercent.Create(15m).Value,
            RiskPerTradePercent.Create(1m).Value,
            RiskRewardRatio.Create(2m).Value,
            _clock).Value;

        _repo.GetActiveAsync(userId, Arg.Any<CancellationToken>()).Returns(existing);
        _repo.MarkSupersededAsync(existing.Id, Arg.Any<IClock>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(userId: userId, capital: 12_000m);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // The MarkSuperseded contract is: it happens BEFORE AddAsync (single UoW).
        Received.InOrder(async () =>
        {
            await _repo.MarkSupersededAsync(existing.Id, Arg.Any<IClock>(), Arg.Any<CancellationToken>());
            await _repo.AddAsync(Arg.Is<RiskProfile>(p => p.CapitalAmount == 12_000m && p.IsActive), Arg.Any<CancellationToken>());
        });
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MarkSupersededFailure_ReturnsConflictWithoutPersisting()
    {
        var userId = Guid.NewGuid();
        var existing = RiskProfile.Create(
            Guid.NewGuid(), userId,
            Money.FromTrusted(5_000m, "USD"),
            MaxDrawdownPercent.Create(15m).Value,
            RiskPerTradePercent.Create(1m).Value,
            RiskRewardRatio.Create(2m).Value,
            _clock).Value;
        var activeId = existing.Id;

        _repo.GetActiveAsync(userId, Arg.Any<CancellationToken>()).Returns(existing);
        _repo.MarkSupersededAsync(activeId, Arg.Any<IClock>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(RiskProfileErrors.ConcurrentSupersede));

        var cmd = ValidCommand(userId: userId);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("conflict.risk_profile.concurrent_supersede");
        await _repo.DidNotReceive().AddAsync(Arg.Any<RiskProfile>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]    // zero capital
    [InlineData(-100)] // negative capital
    public async Task Handle_NonPositiveCapital_ReturnsValidationWithoutPersisting(decimal capital)
    {
        _repo.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskProfile?)null);

        var cmd = ValidCommand(capital: capital);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.risk_profile.capital_amount_invalid");
        await _repo.DidNotReceive().AddAsync(Arg.Any<RiskProfile>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RiskPerTradeAbove5_ReturnsValidation()
    {
        _repo.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskProfile?)null);

        var cmd = ValidCommand(riskPerTrade: 7.5m);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public async Task Handle_DrawdownAbove50_ReturnsValidation()
    {
        _repo.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskProfile?)null);

        var cmd = ValidCommand(maxDrawdown: 60m);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public async Task Handle_RiskRewardBelow1_ReturnsValidation()
    {
        _repo.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskProfile?)null);

        var cmd = ValidCommand(rrTarget: 0.5m);
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.");
    }

    [Fact]
    public async Task Handle_SaveChangesFailure_ReturnsFailure()
    {
        _repo.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RiskProfile?)null);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Result.Failure<int>(Error.Failure("db.update.failed", "DB unhappy.")));

        var cmd = ValidCommand();
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("failure.");
    }
}
