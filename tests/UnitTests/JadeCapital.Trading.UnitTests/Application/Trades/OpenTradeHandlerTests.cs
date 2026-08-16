using JadeCapital.Identity.Contracts.Projections;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.PreTradeChecklists;
using JadeCapital.Trading.Application.Features.Trades.OpenTrade;
using NSubstitute.ReturnsExtensions;

namespace JadeCapital.Trading.UnitTests.Application.Trades;

public class OpenTradeHandlerTests
{
    private readonly ITradeRepository _trades = Substitute.For<ITradeRepository>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IInstrumentRepository _instruments = Substitute.For<IInstrumentRepository>();
    private readonly IPreTradeChecklistRepository _checklists = Substitute.For<IPreTradeChecklistRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIdentityUserRiskProfileReader _riskProfileReader = Substitute.For<IIdentityUserRiskProfileReader>();
    private readonly ILogger<OpenTradeHandler> _logger = Substitute.For<ILogger<OpenTradeHandler>>();

    private static readonly DateTimeOffset FixedNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    private OpenTradeHandler CreateSut() => new(
        _trades, _accounts, _instruments, _checklists, _uow, _clock, _riskProfileReader, _logger);

    private static OpenTradeCommand ValidCommand(
        PreTradeChecklistSubmissionInput? checklist = null) => new(
        UserId: Guid.NewGuid(),
        AccountId: Guid.NewGuid(),
        InstrumentId: Guid.NewGuid(),
        Symbol: "EUR/USD",
        AssetClass: AssetClass.Forex,
        Direction: TradeDirection.Long,
        Volume: 1000m,
        VolumeCurrency: "USD",
        EntryPrice: 1.10m,
        EntryPriceCurrency: "USD",
        Strategy: "trend-following",
        Notes: "Entry at breakout",
        Checklist: checklist);

    private static PreTradeChecklistSubmissionInput ValidChecklist(
        decimal riskRewardAtEntry = 2.5m,
        decimal riskRewardTargetUsed = 2.0m,
        byte confluencesCount = 3)
        => new(Emotionality.Neutral, SetupQuality.Good,
               riskRewardAtEntry, riskRewardTargetUsed, confluencesCount);

    [Fact]
    public async Task Handle_ValidCommand_PersistsAndReturnsTradeDto()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand();
        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Symbol.Should().Be("EUR/USD");
        result.Value.AssetClass.Should().Be(AssetClass.Forex);
        result.Value.Direction.Should().Be(TradeDirection.Long);
        result.Value.Status.Should().Be(TradeStatus.Open);
        result.Value.Volume.Should().Be(1000m);
        result.Value.VolumeCurrency.Should().Be("USD");
        result.Value.EntryPrice.Should().Be(1.10m);
        result.Value.EntryPriceCurrency.Should().Be("USD");
        result.Value.AccountCurrency.Should().Be("USD");
        result.Value.UserId.Should().Be(cmd.UserId);
        result.Value.OpenedAt.Should().Be(FixedNow);

        await _trades.Received(1).AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
        await _checklists.DidNotReceive().AddAsync(Arg.Any<PreTradeChecklist>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidSymbol_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Symbol = "ab" }; // < 3 chars

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().StartWith("validation.symbol");
        await _trades.DidNotReceive().AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ZeroVolume_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { Volume = 0m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        // La validacion la hace Trade.Open (dominio), no Money.Create.
        result.Error.Code.Should().Be("validation.trade.volume_must_be_positive");
        await _trades.DidNotReceive().AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnsupportedVolumeCurrency_ReturnsValidationFailure()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { VolumeCurrency = "ARS" }; // no esta en la whitelist

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.currency.code_unsupported");
        await _trades.DidNotReceive().AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DomainRuleFails_ReturnsDomainError()
    {
        _clock.UtcNow.Returns(FixedNow);

        // EUR/USD pide quote=USD; pasamos entry en EUR -> mismatch.
        var cmd = ValidCommand() with { EntryPriceCurrency = "EUR" };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.trade.entry_price_currency_mismatch");
    }

    [Fact]
    public async Task Handle_RepositoryNotCalled_WhenValidationFails()
    {
        _clock.UtcNow.Returns(FixedNow);

        var cmd = ValidCommand() with { EntryPrice = 0m };

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ============================================
    // Slice 1c.1 — Pre-trade checklist scenarios
    // ============================================

    [Fact]
    public async Task Handle_WithValidChecklist_PersistsTradeAndChecklistInSingleUoW()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(2));
        _riskProfileReader.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new UserRiskProfileSnapshot(10_000m, "USD", 1m, 2m));

        var cmd = ValidCommand(checklist: ValidChecklist());

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        await _trades.Received(1).AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
        await _checklists.Received(1).AddAsync(
            Arg.Is<PreTradeChecklist>(c =>
                c.UserId == cmd.UserId &&
                c.Submission.RiskRewardAtEntry == 2.5m &&
                c.Submission.RiskRewardTargetUsed == 2.0m &&
                c.Submission.ConfluencesCount == 3 &&
                c.Submission.Emotionality == Emotionality.Neutral &&
                c.Submission.SetupQuality == SetupQuality.Good),
            Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithChecklistRrBelowTarget_ReturnsFailureAndDoesNotPersist()
    {
        _clock.UtcNow.Returns(FixedNow);
        _riskProfileReader.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new UserRiskProfileSnapshot(10_000m, "USD", 1m, 2m)); // target=2.0

        // RR at entry=1.5 < target=2.0 -> debe fallar.
        var cmd = ValidCommand(checklist: ValidChecklist(
            riskRewardAtEntry: 1.5m,
            riskRewardTargetUsed: 2.0m));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.pre_trade_checklist.rr_below_target");

        await _trades.DidNotReceive().AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
        await _checklists.DidNotReceive().AddAsync(Arg.Any<PreTradeChecklist>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithChecklistAndNoActiveProfile_FallsBackToDefaultTarget()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(2));
        // No hay perfil activo -> reader retorna null.
        _riskProfileReader.GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((UserRiskProfileSnapshot?)null);

        // RR at entry=1.5 + target=1.0 (default cuando no hay perfil).
        // El aggregate acepta RR >= target.
        var cmd = ValidCommand(checklist: ValidChecklist(
            riskRewardAtEntry: 1.5m,
            riskRewardTargetUsed: 1.0m));

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _checklists.Received(1).AddAsync(
            Arg.Is<PreTradeChecklist>(c =>
                c.Submission.RiskRewardAtEntry == 1.5m &&
                c.Submission.RiskRewardTargetUsed == 1.0m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutChecklist_LegacyPath_DoesNotTouchReaderOrRepository()
    {
        _clock.UtcNow.Returns(FixedNow);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Success(1));

        var cmd = ValidCommand(checklist: null);

        var result = await CreateSut().Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _trades.Received(1).AddAsync(Arg.Any<Trade>(), Arg.Any<CancellationToken>());
        await _checklists.DidNotReceive().AddAsync(Arg.Any<PreTradeChecklist>(), Arg.Any<CancellationToken>());
        await _riskProfileReader.DidNotReceive().GetActiveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}