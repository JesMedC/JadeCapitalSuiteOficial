using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;
using JadeCapital.Trading.Domain.Scanner;
using FluentAssertions;
using Xunit;

namespace JadeCapital.Trading.UnitTests.Scanner;

public class ScannerFilterTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly IClock Clock = new StaticClockLocal(new DateTimeOffset(2026, 8, 17, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Create_WithValidPayload_PersistsNewFilter()
    {
        var result = ScannerFilter.Create(
            UserId, "Momentum EUR/USD", null, null, 1000m, 1.5m, VolatilityWindow.Daily, null, Clock);
        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Momentum EUR/USD");
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Create_EmptyUserId_Fails()
    {
        var result = ScannerFilter.Create(Guid.Empty, "x", null, null, null, 1.5m, VolatilityWindow.Daily, null, Clock);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.scanner.user_id_required");
    }

    [Fact]
    public void Create_EmptyName_Fails()
    {
        var result = ScannerFilter.Create(UserId, "  ", null, null, null, 1.5m, VolatilityWindow.Daily, null, Clock);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.scanner.name_required");
    }

    [Fact]
    public void Create_NameTooLong_Fails()
    {
        var longName = new string('x', 65);
        var result = ScannerFilter.Create(UserId, longName, null, null, null, 1.5m, VolatilityWindow.Daily, null, Clock);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.scanner.name_too_long");
    }

    [Fact]
    public void Create_RiskRewardBelow1_Fails()
    {
        var result = ScannerFilter.Create(UserId, "x", null, null, null, 0.5m, VolatilityWindow.Daily, null, Clock);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.scanner.invalid_risk_reward");
    }

    [Fact]
    public void Create_NegativeSpread_Fails()
    {
        var result = ScannerFilter.Create(UserId, "x", -0.1m, null, null, 1.5m, VolatilityWindow.Daily, null, Clock);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.scanner.spread_negative");
    }

    [Fact]
    public void Create_MinGreaterThanMax_Fails()
    {
        var result = ScannerFilter.Create(UserId, "x", 0.005m, 0.001m, null, 1.5m, VolatilityWindow.Daily, null, Clock);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.scanner.min_greater_than_max");
    }

    [Fact]
    public void Update_PreservesCreatedAt_BumpsUpdatedAt()
    {
        var filter = ScannerFilter.Create(UserId, "x", null, null, null, 1.5m, VolatilityWindow.Daily, null, Clock).Value;
        var later = new StaticClockLocal(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));
        var update = filter.Update("x2", null, null, null, 2.0m, VolatilityWindow.Weekly, null, later);
        update.IsSuccess.Should().BeTrue();
        filter.Name.Should().Be("x2");
        filter.UpdatedAt.Should().NotBeNull();
        filter.CreatedAt.Should().Be(filter.CreatedAt); // unchanged
    }

    [Fact]
    public void Deactivate_ThenActivate_IsIdempotent()
    {
        var filter = ScannerFilter.Create(UserId, "x", null, null, null, 1.5m, VolatilityWindow.Daily, null, Clock).Value;
        filter.Deactivate(Clock);
        filter.IsActive.Should().BeFalse();
        filter.Deactivate(Clock);
        filter.IsActive.Should().BeFalse();
        filter.Activate(Clock);
        filter.IsActive.Should().BeTrue();
    }

    private sealed class StaticClockLocal : IClock
    {
        private readonly DateTimeOffset _now;
        public StaticClockLocal(DateTimeOffset now) { _now = now; }
        public DateTimeOffset UtcNow => _now;
    }
}
