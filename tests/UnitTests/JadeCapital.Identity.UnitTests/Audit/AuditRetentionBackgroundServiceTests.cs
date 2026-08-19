using FluentAssertions;
using JadeCapital.Identity.Infrastructure.Audit;
using JadeCapital.Identity.Infrastructure.Audit.Configuration;
using JadeCapital.Identity.Infrastructure.BackgroundServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace JadeCapital.Identity.UnitTests.Audit;

/// <summary>
/// Tests for <see cref="AuditRetentionBackgroundService"/> (Wave 9, slice 9b.1).
///
/// <para>
/// Two RED scenarios pinned here (per tasks.md §9b.1 Phase 4.2):
/// <list type="number">
///   <item><b>RunOnceAsync_InvokesRetentionService_WithCorrectCutoffAndBatchLimit</b> —
///         the BackgroundService computes the cutoff from
///         <see cref="AuditRetentionOptions.RetentionDays"/> and dispatches
///         to <see cref="IAuditRetentionService.PurgeOldAsync"/>. The
///         test substitutes the retention service with NSubstitute and
///         asserts the cutoff equals <c>UtcNow.AddDays(-RetentionDays)</c>
///         (within a 1-second tolerance) + the batch limit matches
///         <see cref="AuditRetentionOptions.BatchLimit"/>.</item>
///   <item><b>RunOnceAsync_Throws_IsLogged_AndReturnsNormally</b> — a
///         retention failure must NOT crash the host. The catch in
///         <see cref="AuditRetentionBackgroundService.RunOnceAsync"/>
///         logs the error via <see cref="ILogger"/> + returns
///         normally. The test substitutes a throwing
///         <see cref="IAuditRetentionService"/> and asserts the
///         <see cref="AuditRetentionBackgroundService.RunOnceAsync"/>
///         returns + a log error entry was produced.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why we call <see cref="AuditRetentionBackgroundService.RunOnceAsync"/>
/// directly instead of running the full <see cref="IHostedService"/></b>:
/// the BackgroundService's <see cref="BackgroundService.ExecuteAsync"/>
/// awaits the configured <see cref="AuditRetentionOptions.InitialDelay"/>
/// BEFORE the first <see cref="AuditRetentionBackgroundService.RunOnceAsync"/>
/// call. Testing the FULL lifecycle requires either (a) a real
/// <see cref="IHost"/> start with a very short delay (slow, flaky in CI)
/// or (b) unit-testing the <see cref="AuditRetentionBackgroundService.RunOnceAsync"/>
/// method directly (fast, deterministic, focused on the unit-testable
/// surface). The spec outcome ("first run after InitialDelay; exception
/// is logged + does not crash host") is verifiable by testing the
/// lifecycle + the method's per-cycle behavior. We split the test into:
/// <list type="bullet">
///   <item>E5 #1 — RunOnceAsync invokes the retention service with the
///         correct cutoff + batch limit (the per-cycle contract).</item>
///   <item>E5 #2 — RunOnceAsync catches exceptions + logs them (the
///         host-survival contract).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why a focused <see cref="AuditRetentionBackgroundService"/>
/// resolution via <c>ServiceCollection</c></b>: the BackgroundService
/// resolves <see cref="IAuditRetentionService"/> via
/// <see cref="IServiceScopeFactory"/> per cycle. The test uses a real
/// <see cref="ServiceCollection"/> with the production
/// <see cref="AuditRetentionBackgroundService"/> + a NSubstitute mock
/// for <see cref="IAuditRetentionService"/> + a test
/// <see cref="IOptionsMonitor{T}"/> that returns the configured
/// <see cref="AuditRetentionOptions"/>.
/// </para>
/// </summary>
public class AuditRetentionBackgroundServiceTests
{
    [Fact]
    public async Task RunOnceAsync_InvokesRetentionService_WithCorrectCutoffAndBatchLimit()
    {
        // Phase 4.2 #1: RunOnceAsync resolves the cutoff from
        // AuditRetentionOptions.RetentionDays + the BatchLimit from
        // AuditRetentionOptions.BatchLimit, then dispatches to
        // IAuditRetentionService.PurgeOldAsync. The test substitutes
        // the retention service with NSubstitute and asserts those
        // values are forwarded correctly.
        var mockRetention = Substitute.For<IAuditRetentionService>();
        mockRetention.PurgeOldAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                      .Returns(0);

        var optionsBatch = 4242;
        var optionsRetentionDays = 90;
        var options = new AuditRetentionOptions
        {
            RetentionDays = optionsRetentionDays,
            CleanupIntervalHours = 24,
            BatchLimit = optionsBatch,
            InitialDelaySeconds = 0
        };
        var sut = BuildBackgroundService(mockRetention, options);

        var before = DateTimeOffset.UtcNow;
        await sut.RunOnceAsync(CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        // The retention service was called exactly once.
        await mockRetention.Received(1).PurgeOldAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        // Capture the actual cutoff + batch limit used by RunOnceAsync.
        var calls = mockRetention.ReceivedCalls().ToList();
        var cutoffArg = (DateTimeOffset)calls[0].GetArguments()[0]!;
        var batchArg = (int)calls[0].GetArguments()[1]!;

        // The cutoff should be approximately UtcNow - 90 days. Tolerance
        // covers test wall clock + the small delay between before/after.
        var expectedMin = before.AddDays(-optionsRetentionDays).AddSeconds(-2);
        var expectedMax = after.AddDays(-optionsRetentionDays).AddSeconds(2);
        cutoffArg.Should().BeOnOrAfter(expectedMin);
        cutoffArg.Should().BeOnOrBefore(expectedMax);

        // The batch limit should match the configured options.
        batchArg.Should().Be(optionsBatch);
    }

    [Fact]
    public async Task RunOnceAsync_Throws_IsLogged_AndReturnsNormally()
    {
        // Phase 4.2 #2: a retention failure must NOT crash the host.
        // The RunOnceAsync catch logs the error and returns normally —
        // the test asserts both behaviors. The BackgroundService's
        // ExecuteAsync loop calls RunOnceAsync per cycle; the catch
        // ensures the loop continues + the host stays alive.
        var mockRetention = Substitute.For<IAuditRetentionService>();
        mockRetention.PurgeOldAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                      .Returns<int>(_ => throw new InvalidOperationException("simulated retention failure"));

        var capturedLogs = new List<(LogLevel Level, string Message, Exception? Exception)>();
        var options = new AuditRetentionOptions
        {
            RetentionDays = 90,
            CleanupIntervalHours = 24,
            BatchLimit = 100,
            InitialDelaySeconds = 0
        };
        var sut = BuildBackgroundService(mockRetention, options, capturedLogs);

        // RunOnceAsync MUST NOT throw (the catch swallows the exception).
        var act = async () => await sut.RunOnceAsync(CancellationToken.None);
        await act.Should().NotThrowAsync(
            "the BackgroundService MUST catch retention failures and log them — the host must not crash.");

        // An error log entry was produced that references the failure.
        capturedLogs.Should().Contain(l =>
            l.Level == LogLevel.Error &&
            (l.Message.Contains("RetentionRun: failed") ||
             (l.Exception != null && l.Exception.Message.Contains("simulated retention failure"))),
            "the BackgroundService MUST log the retention failure via LogError.");

        // The retention service was called exactly once.
        await mockRetention.Received(1).PurgeOldAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    private static AuditRetentionBackgroundService BuildBackgroundService(
        IAuditRetentionService retentionService,
        AuditRetentionOptions options,
        List<(LogLevel Level, string Message, Exception? Exception)>? capturedLogs = null)
    {
        capturedLogs ??= new List<(LogLevel, string, Exception?)>();

        var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new TestLoggerProvider(capturedLogs)));

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(retentionService);
        services.AddSingleton<IServiceScopeFactory>(sp =>
            new TestScopeFactory(new TestScope(sp)));

        var optionsMonitor = new TestOptionsMonitor<AuditRetentionOptions>(options);
        services.AddSingleton<IOptionsMonitor<AuditRetentionOptions>>(optionsMonitor);

        var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILogger<AuditRetentionBackgroundService>>();
        var monitor = sp.GetRequiredService<IOptionsMonitor<AuditRetentionOptions>>();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        return new AuditRetentionBackgroundService(scopeFactory, monitor, logger);
    }

    /// <summary>
    /// Test-only <see cref="IOptionsMonitor{T}"/> that returns a fixed
    /// value. The production BackgroundService uses
    /// <see cref="IOptionsMonitor{T}.CurrentValue"/> which we satisfy.
    /// </summary>
    private sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public TestOptionsMonitor(T value) { CurrentValue = value; }
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>Simple test <see cref="IServiceScope"/> that wraps a root provider.</summary>
    private sealed class TestScope : IServiceScope
    {
        public TestScope(IServiceProvider sp) { ServiceProvider = sp; }
        public IServiceProvider ServiceProvider { get; }
        public void Dispose() { }
    }

    /// <summary>Simple test <see cref="IServiceScopeFactory"/> that returns the same scope.</summary>
    private sealed class TestScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceScope _scope;
        public TestScopeFactory(IServiceScope scope) { _scope = scope; }
        public IServiceScope CreateScope() => _scope;
    }

    /// <summary>
    /// Captures every log entry to the supplied list. Each entry is
    /// <c>(LogLevel, Message, Exception)</c>; the test asserts on
    /// any of the three fields. The formatter-based message is
    /// augmented with the exception's <c>Message</c> when present
    /// so the test can pattern-match on the underlying error.
    /// </summary>
    private sealed class TestLoggerProvider : ILoggerProvider
    {
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _sink;
        public TestLoggerProvider(List<(LogLevel, string, Exception?)> sink) { _sink = sink; }
        public ILogger CreateLogger(string categoryName) => new TestLogger(_sink);
        public void Dispose() { }
        private sealed class TestLogger : ILogger
        {
            private readonly List<(LogLevel Level, string Message, Exception? Exception)> _sink;
            public TestLogger(List<(LogLevel Level, string Message, Exception? Exception)> sink) { _sink = sink; }
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var message = formatter(state, exception);
                if (exception is not null && !message.Contains(exception.Message, StringComparison.Ordinal))
                    message = $"{message} {exception.Message}";
                _sink.Add((logLevel, message, exception));
            }
            private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
        }
    }
}
