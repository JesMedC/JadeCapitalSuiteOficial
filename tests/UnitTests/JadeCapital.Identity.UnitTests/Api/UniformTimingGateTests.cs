using JadeCapital.Identity.Api;
using Microsoft.Extensions.Time.Testing;

namespace JadeCapital.Identity.UnitTests.Api;

public class UniformTimingGateTests
{
    [Fact]
    public async Task BeginAndAwait_UseIndependentBoundedMonotonicDeadlines()
    {
        var time = new FakeTimeProvider();
        var gate = new UniformTimingGate(time, Substitute.For<ILogger<UniformTimingGate>>());
        var deadlines = Enumerable.Range(0, 32).Select(_ => gate.Begin()).ToArray();

        deadlines.Should().OnlyContain(d => d.Target >= TimeSpan.FromSeconds(13.75)
            && d.Target <= TimeSpan.FromSeconds(14.25));
        deadlines.Select(d => d.Target).Distinct().Should().HaveCountGreaterThan(1);

        var deadline = deadlines[0];
        time.Advance(TimeSpan.FromSeconds(4));
        var waiting = gate.AwaitAsync(deadline);
        waiting.IsCompleted.Should().BeFalse();

        time.Advance(deadline.Target - TimeSpan.FromSeconds(4) - TimeSpan.FromMilliseconds(1));
        waiting.IsCompleted.Should().BeFalse();
        time.Advance(TimeSpan.FromMilliseconds(1));
        await waiting;
    }

    [Fact]
    public async Task Await_OverrunDoesNotDelay_AndCancellationPropagates()
    {
        var time = new FakeTimeProvider();
        var logger = Substitute.For<ILogger<UniformTimingGate>>();
        var gate = new UniformTimingGate(time, logger);
        var overrun = gate.Begin();
        time.Advance(overrun.Target + TimeSpan.FromMilliseconds(1));

        var completed = gate.AwaitAsync(overrun);

        completed.IsCompletedSuccessfully.Should().BeTrue();
        await completed;
        logger.ReceivedCalls().Should().ContainSingle(call =>
            call.GetMethodInfo().Name == nameof(ILogger.Log)
            && ((EventId)call.GetArguments()[1]!).Name == "UniformTimingOverrun");

        using var cts = new CancellationTokenSource();
        var cancelled = gate.AwaitAsync(gate.Begin(), cts.Token);
        cancelled.IsCompleted.Should().BeFalse();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }
}
