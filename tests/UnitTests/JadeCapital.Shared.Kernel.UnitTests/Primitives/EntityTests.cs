using Xunit;
using FluentAssertions;
using JadeCapital.Shared.Kernel.Primitives;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Shared.Kernel.UnitTests.Primitives;

public class EntityTests
{
    private sealed class TestEntity : Entity<Guid>
    {
        public TestEntity(Guid id) : base(id) { }
        public void DoTouch() => Touch();
    }

    [Fact]
    public void Constructor_SetsIdAndCreatedAt()
    {
        var id = Guid.NewGuid();
        var sut = new TestEntity(id);

        sut.Id.Should().Be(id);
        sut.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        sut.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Touch_SetsUpdatedAt()
    {
        var sut = new TestEntity(Guid.NewGuid());
        sut.DoTouch();

        sut.UpdatedAt.Should().NotBeNull();
        sut.UpdatedAt.Should().BeAfter(sut.CreatedAt);
    }

    [Fact]
    public void Equality_BasedOnTypeAndId()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new TestEntity(id);

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentId_NotEqual()
    {
        var a = new TestEntity(Guid.NewGuid());
        var b = new TestEntity(Guid.NewGuid());

        a.Should().NotBe(b);
    }

    [Fact]
    public void Equality_DifferentType_SameId_NotEqual()
    {
        var id = Guid.NewGuid();
        var a = new TestEntity(id);
        var b = new OtherEntity(id);

        a.Should().NotBe(b);
    }

    private sealed class OtherEntity : Entity<Guid>
    {
        public OtherEntity(Guid id) : base(id) { }
    }
}

public class ValueObjectTests
{
    private sealed class MoneyLike : ValueObject
    {
        public decimal Amount { get; }
        public string Currency { get; }
        public MoneyLike(decimal amount, string currency) { Amount = amount; Currency = currency; }
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Equality_BasedOnComponents()
    {
        var a = new MoneyLike(100m, "USD");
        var b = new MoneyLike(100m, "USD");
        var c = new MoneyLike(100m, "EUR");

        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
        a.Should().NotBe(c);
    }
}

public class AggregateRootTests
{
    private sealed record TestEvent(DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate(Guid id) : base(id) { }
        public void DoSomething()
        {
            RaiseDomainEvent(new TestEvent(DateTimeOffset.UtcNow));
        }
    }

    [Fact]
    public void RaiseDomainEvent_AddsToCollection()
    {
        var agg = new TestAggregate(Guid.NewGuid());
        agg.DoSomething();

        agg.DomainEvents.Should().HaveCount(1);
        agg.DomainEvents.First().Should().BeOfType<TestEvent>();
    }

    [Fact]
    public void ClearDomainEvents_RemovesAll()
    {
        var agg = new TestAggregate(Guid.NewGuid());
        agg.DoSomething();
        agg.DoSomething();

        agg.ClearDomainEvents();

        agg.DomainEvents.Should().BeEmpty();
    }
}

public class ClockTests
{
    [Fact]
    public void SystemClock_ReturnsUtcNow()
    {
        IClock clock = new SystemClock();

        clock.UtcNow.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
        clock.UtcNow.Offset.Should().Be(TimeSpan.Zero);
    }
}