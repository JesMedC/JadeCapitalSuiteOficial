using JadeCapital.Shared.Kernel.MarketData;
using JadeCapital.Shared.Kernel.Realtime;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Infrastructure.Realtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using FluentAssertions;

namespace JadeCapital.Trading.UnitTests.Realtime;

// ============================================================================
//  QuoteBroadcastService tests — slice 4c (Realtime).
//
//  The service polls IQuoteProvider every 5s and pushes ticks to
//  SignalR groups. Tests drive BroadcastTickAsync directly so they
//  don't depend on the BackgroundService loop timing.
//
//  Uses a capturing IQuoteClient + IHubClients fake instead of NSubstitute
//  for the hub layer — NSubstitute's `Received(...).AsyncMethod()` chains
//  have quirky return-type inference for Task-returning interface methods.
// ============================================================================

public class QuoteBroadcastServiceTests
{
    private static readonly DateTimeOffset FixedTs = new(2026, 8, 19, 14, 32, 0, TimeSpan.Zero);

    private readonly IQuoteSubscriptionRegistry _registry = new InMemoryQuoteSubscriptionRegistry();
    private readonly CapturingHubClients _clients = new();
    private readonly IHubContext<QuoteHub, IQuoteClient> _hubContext;
    private readonly CapturingScopeFactory _scopeFactory = new();

    public QuoteBroadcastServiceTests()
    {
        _hubContext = Substitute.For<IHubContext<QuoteHub, IQuoteClient>>();
        _hubContext.Clients.Returns(_clients);
    }

    private QuoteBroadcastService NewSut()
        => new(_scopeFactory, _hubContext, _registry, NullLogger<QuoteBroadcastService>.Instance, TimeSpan.FromMilliseconds(50));

    private static Quote Quote(string symbol, decimal bid, decimal ask)
        => new(symbol, bid, ask, ask - bid, 100_000m, FixedTs, QuoteSource.Stub);

    [Fact]
    public async Task Tick_NoSubscribers_NoProviderCall_NoBroadcast()
    {
        var provider = Substitute.For<IQuoteProvider>();
        _scopeFactory.Register(provider);
        var sut = NewSut();

        await sut.BroadcastTickAsync(CancellationToken.None);

        await provider.DidNotReceive().GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>());
        _clients.GroupUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Tick_WithSubscribers_FetchesQuotesFromProvider_AndPushesPerSymbolGroup()
    {
        _registry.Add("conn-1", new[] { "EURUSD", "GBPJPY" });

        var provider = Substitute.For<IQuoteProvider>();
        provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Quote> { Quote("EURUSD", 1.0850m, 1.0851m), Quote("GBPJPY", 154.20m, 154.22m) });
        _scopeFactory.Register(provider);

        var sut = NewSut();

        await sut.BroadcastTickAsync(CancellationToken.None);

        await provider.Received(1).GetQuotesAsync(
            Arg.Is<IEnumerable<string>>(s => s.OrderBy(x => x).SequenceEqual(new[] { "EURUSD", "GBPJPY" })),
            Arg.Any<CancellationToken>());

        _clients.GroupUpdates.Should().HaveCount(2);
        _clients.GroupUpdates.Should().Contain(u => u.Symbol == "EURUSD" && u.Bid == 1.0850m);
        _clients.GroupUpdates.Should().Contain(u => u.Symbol == "GBPJPY" && u.Bid == 154.20m);
        _clients.AllGroupNames.Should().BeEquivalentTo(new[] { "symbol-EURUSD", "symbol-GBPJPY" });
    }

    [Fact]
    public async Task Tick_UnchangedQuoteSincePreviousTick_DoesNotPush()
    {
        _registry.Add("conn-1", new[] { "EURUSD" });

        var provider = Substitute.For<IQuoteProvider>();
        provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Quote> { Quote("EURUSD", 1.0850m, 1.0851m) });
        _scopeFactory.Register(provider);

        var sut = NewSut();

        await sut.BroadcastTickAsync(CancellationToken.None);   // first tick — pushes
        await sut.BroadcastTickAsync(CancellationToken.None);   // second tick — same quote → skip

        _clients.GroupUpdates.Should().HaveCount(1);
    }

    [Fact]
    public async Task Tick_QuoteChangedBetweenTicks_PushesTheUpdate()
    {
        _registry.Add("conn-1", new[] { "EURUSD" });

        var provider = Substitute.For<IQuoteProvider>();
        provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Quote> { Quote("EURUSD", 1.0850m, 1.0851m) },
                     new List<Quote> { Quote("EURUSD", 1.0860m, 1.0861m) });
        _scopeFactory.Register(provider);

        var sut = NewSut();

        await sut.BroadcastTickAsync(CancellationToken.None);
        await sut.BroadcastTickAsync(CancellationToken.None);

        _clients.GroupUpdates.Should().HaveCount(2);
        _clients.GroupUpdates[0].Bid.Should().Be(1.0850m);
        _clients.GroupUpdates[1].Bid.Should().Be(1.0860m);
    }

    [Fact]
    public async Task Tick_ProviderThrows_SwallowsExceptionAndContinues()
    {
        _registry.Add("conn-1", new[] { "EURUSD" });

        var provider = Substitute.For<IQuoteProvider>();
        provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<Quote>>(new InvalidOperationException("feed down")));
        _scopeFactory.Register(provider);

        var sut = NewSut();

        var act = () => sut.BroadcastTickAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        _clients.GroupUpdates.Should().BeEmpty();
    }

    [Fact]
    public async Task Tick_PartialProviderResult_DispatchesOnlyKnownSymbols()
    {
        _registry.Add("conn-1", new[] { "EURUSD", "ZZZZZ" });

        var provider = Substitute.For<IQuoteProvider>();
        provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Quote> { Quote("EURUSD", 1.0850m, 1.0851m) }); // ZZZZ dropped
        _scopeFactory.Register(provider);

        var sut = NewSut();

        await sut.BroadcastTickAsync(CancellationToken.None);

        _clients.AllGroupNames.Should().NotContain("symbol-ZZZZZ");
        _clients.AllGroupNames.Should().Contain("symbol-EURUSD");
    }

    [Fact]
    public async Task Tick_PushesCacheUpsertForEachDispatchedSymbol()
    {
        _registry.Add("conn-1", new[] { "EURUSD" });

        var provider = Substitute.For<IQuoteProvider>();
        provider.GetQuotesAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Quote> { Quote("EURUSD", 1.0850m, 1.0851m) });
        _scopeFactory.Register(provider);

        var cache = Substitute.For<IQuoteCacheRepository>();
        _scopeFactory.Register(cache);

        var sut = NewSut();

        await sut.BroadcastTickAsync(CancellationToken.None);

        await cache.Received(1).UpsertAsync(
            Arg.Is<Quote>(q => q.Symbol == "EURUSD"),
            Arg.Any<CancellationToken>());
    }

    // ===== Fakes =====

    /// <summary>
    /// Captures every OnQuoteUpdate invocation for assertions. Avoids the
    /// NSubstitute `Received(1).OnQuoteUpdate(...)` Task-returning chain
    /// which has spotty type inference for interface substitutes.
    /// </summary>
    private sealed class CapturingQuoteClient : IQuoteClient
    {
        public List<QuoteUpdate> Updates { get; } = new();
        public List<(string Code, string Message)> Errors { get; } = new();
        public Task OnQuoteUpdate(QuoteUpdate update) { Updates.Add(update); return Task.CompletedTask; }
        public Task OnError(string code, string message) { Errors.Add((code, message)); return Task.CompletedTask; }
    }

    private sealed class CapturingHubClients : IHubClients<IQuoteClient>
    {
        private readonly CapturingQuoteClient _all = new();
        private readonly Dictionary<string, CapturingQuoteClient> _groups = new();

        public List<QuoteUpdate> AllUpdates => _all.Updates;
        public List<QuoteUpdate> GroupUpdates => _groups.Values.SelectMany(g => g.Updates).ToList();
        public List<string> AllGroupNames { get; } = new();

        public IQuoteClient All => _all;
        public IQuoteClient AllExcept(IReadOnlyList<string> excludedConnectionIds) => _all;
        public IQuoteClient Client(string connectionId) => _all;
        public IQuoteClient Clients(IReadOnlyList<string> connectionIds) => _all;
        public IQuoteClient Group(string groupName)
        {
            AllGroupNames.Add(groupName);
            if (!_groups.TryGetValue(groupName, out var c))
            {
                c = new CapturingQuoteClient();
                _groups[groupName] = c;
            }
            return c;
        }
        public IQuoteClient Group(IReadOnlyList<string> groupNames) => _all;
        public IQuoteClient Groups(IReadOnlyList<string> groupNames) => _all;
        public IQuoteClient GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Group(groupName);
        public IQuoteClient User(string userId) => _all;
        public IQuoteClient Users(IReadOnlyList<string> userIds) => _all;
    }

    /// <summary>
    /// Stub IServiceScopeFactory that resolves any registered service back
    /// to the same instance (no EF context per scope). Lets tests wire
    /// provider + cache + repos via Register().
    /// </summary>
    private sealed class CapturingScopeFactory : IServiceScopeFactory
    {
        private readonly Dictionary<Type, object> _services = new();

        public void Register<T>(T instance) where T : class => _services[typeof(T)] = instance;

        public IServiceScope CreateAsyncScope() => new CapturingScope(_services);
        public IServiceScope CreateScope() => new CapturingScope(_services);

        private sealed class CapturingScope : IServiceScope
        {
            private readonly CapturingProvider _provider;

            public CapturingScope(Dictionary<Type, object> services)
            {
                _provider = new CapturingProvider(services);
            }

            public IServiceProvider ServiceProvider => _provider;

            public void Dispose() { }
            public static ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }

        private sealed class CapturingProvider : IServiceProvider
        {
            private readonly Dictionary<Type, object> _services;
            public CapturingProvider(Dictionary<Type, object> services) { _services = services; }

            public object? GetService(Type serviceType) =>
                _services.TryGetValue(serviceType, out var svc) ? svc : null;
        }
    }
}