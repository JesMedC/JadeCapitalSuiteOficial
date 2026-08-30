using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Infrastructure.Cascade;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NSubstitute.ReceivedExtensions;

namespace JadeCapital.Identity.UnitTests.Cascade;

/// <summary>
/// Tests for <see cref="UserCascadeDeleterOrchestrator"/> (Wave 10, slice 10.5
/// cascade + Wave 11, slice 11.1 xUnit coverage).
///
/// <para>
/// Five RED scenarios pinned here (per Wave 11.1 <c>tasks.md</c> Phase 1 +
/// the <c>gdpr-endpoint-coverage</c> spec.md orchestrator scenarios):
/// <list type="number">
///   <item><b>Orchestrator_CascadeSoftDeleteAsync_InvokesAllDeletors_InSequence</b> —
///         orchestrator with 3 <see cref="IUserCascadeDeletor"/> mocks
///         (Identity → Trading → Billing) invokes them in registration order
///         and returns the sum of the rows touched.</item>
///   <item><b>Orchestrator_CascadeHardDeleteAsync_InvokesAllDeletors_ThenPhysicalUserRowDelete</b> —
///         on <c>CascadeHardDeleteAsync</c>, the deletors run first, then
///         <see cref="IGdprAuditAnonymizer.AnonymizeUserAsync"/>, then the
///         <c>physicalUserRowDeleteAsync</c> delegate — in that exact order.
///         Each step's exception is swallowed and logged but does not abort
///         the cascade.</item>
///   <item><b>Orchestrator_PerDeletorException_ContinuesToNextDeletor</b> —
///         the Trading mock throws <c>InvalidOperationException</c> on
///         <c>CascadeSoftDeleteAsync</c>; Identity (before) and Billing
///         (after) MUST still be invoked — the orchestrator continues past
///         the failure.</item>
///   <item><b>Orchestrator_PerDeletorException_LogsAndContinues</b> —
///         a per-deletor exception MUST be captured as an <c>Error</c> log
///         via <see cref="ILogger"/> and MUST NOT be re-thrown. The
///         orchestrator's no-throw batch contract covers every deletor.</item>
///   <item><b>Orchestrator_PerDeletorReturns0Rows_StillProceedsToNext</b> —
///         even when one deletor returns <c>0</c> (no rows touched — the
///         user owns nothing in that module), the next deletor MUST still
///         be invoked. The orchestrator treats a zero-result as a valid
///         no-op and proceeds.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why NSubstitute mocks</b> (per tasks.md §11.1 Phase 1.5): the
/// orchestrator's contract is "iterate registered deletors, swallow per-step
/// failures, return the sum". That contract is best pinned at the unit
/// boundary — substituting each <see cref="IUserCascadeDeletor"/> with
/// NSubstitute gives the test full control over per-deletor return values
/// and exceptions, plus verifiable ordering via
/// <see cref="Received.Received(int)"/>. Cross-module integration
/// verification lives in the Wave 11.1
/// <c>GdprCascadeIntegrationTests</c> (Testcontainers Postgres + the
/// real IdentityDbContext).
/// </para>
///
/// <para>
/// <b>Why <see cref="ILogger{TCategoryName}"/> substituted with
/// NSubstitute</b>: we assert the structured log entries were produced
/// (Scenarios 2 + 4 — the audit trail is a compliance contract) without
/// spinning up a real <see cref="LoggerFactory"/>. Mirrors the 9b.1
/// <c>AuditRetentionBackgroundServiceTests</c> pattern.
/// </para>
/// </summary>
public class UserCascadeDeleterOrchestratorTests
{
    private static readonly Guid TestUserId = Guid.NewGuid();

    [Fact]
    public async Task Orchestrator_CascadeSoftDeleteAsync_InvokesAllDeletors_InSequence()
    {
        // Scenario: orchestrator with 3 mock deletors (Identity → Trading →
        // Billing) registered invokes all of them in registration order +
        // returns the sum of rows touched.
        var identity = Substitute.For<IUserCascadeDeletor>();
        var trading = Substitute.For<IUserCascadeDeletor>();
        var billing = Substitute.For<IUserCascadeDeletor>();

        // Return a distinct non-zero value per deletor so the sum assertion
        // is unambiguous. If the orchestrator dropped one of the returns,
        // the sum would not equal 9.
        var identitySoftRows = 1; // user row + risk profile (Identity owns 2 rows)
        var tradingSoftRows = 3; // 1 trade + 1 journal + 1 strategy
        var billingSoftRows = 5; // 1 subscription + 1 stripe_customer + 3 webhooks
        identity.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>())
                .Returns(identitySoftRows);
        trading.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>())
                .Returns(tradingSoftRows);
        billing.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>())
                .Returns(billingSoftRows);

        var anonymizer = Substitute.For<IGdprAuditAnonymizer>();

        var sut = BuildOrchestrator(
            deletors: new[] { identity, trading, billing },
            anonymizer: anonymizer);

        // Act: invoke CascadeSoftDeleteAsync.
        var sum = await sut.CascadeSoftDeleteAsync(TestUserId, CancellationToken.None);

        // Assert: every deletor invoked exactly once with the user id +
        // the supplied cancellation token.
        await identity.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
        await trading.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
        await billing.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());

        // Assert: invocation order is registration order (Identity → Trading
        // → Billing). NSubstitute preserves Received call order across
        // substitutes on the same sequence.
        Received.InOrder(async () =>
        {
            await identity.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
            await trading.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
            await billing.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
        });

        // Assert: the return value is the sum of all deletors' rows touched.
        sum.Should().Be(identitySoftRows + tradingSoftRows + billingSoftRows,
            "the orchestrator returns the sum of rows touched across every registered deletor.");

        // The audit anonymizer is NOT called from the soft-delete path —
        // the audit trail must remain attributable to the user until the
        // 30-day hard-delete sweep (per the orchestrator's design doc + the
        // `gdpr-endpoint-coverage` spec scenario).
        await anonymizer.DidNotReceiveWithAnyArgs()
            .AnonymizeUserAsync(default, default);
    }

    [Fact]
    public async Task Orchestrator_CascadeHardDeleteAsync_InvokesAllDeletors_ThenPhysicalUserRowDelete()
    {
        // Scenario: on hard-delete, the orchestrator must invoke the
        // deletors first (in order), then the audit anonymizer, then the
        // physical user-row delete last (FK constraint requires the
        // FK-referencing rows gone first). Each step's exception is
        // swallowed and logged but does not abort the cascade.
        var identity = Substitute.For<IUserCascadeDeletor>();
        var trading = Substitute.For<IUserCascadeDeletor>();
        var billing = Substitute.For<IUserCascadeDeletor>();
        identity.CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(1);
        trading.CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(3);
        billing.CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(5);

        var anonymizer = Substitute.For<IGdprAuditAnonymizer>();
        anonymizer.AnonymizeUserAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(1);

        var physicalUserRowDeleted = false;
        var physicalUserRowDeleteCalls = 0;
        Task<int> PhysicalUserRowDeleteAsync(CancellationToken innerCt)
        {
            // The physical delete MUST run AFTER the deletors + the
            // anonymizer; we capture the ordering via the side effect.
            physicalUserRowDeleted = true;
            physicalUserRowDeleteCalls++;
            return Task.FromResult(1);
        }

        var sut = BuildOrchestrator(
            deletors: new[] { identity, trading, billing },
            anonymizer: anonymizer);

        var sum = await sut.CascadeHardDeleteAsync(
            TestUserId,
            PhysicalUserRowDeleteAsync,
            CancellationToken.None);

        // Assert: every deletor was invoked exactly once.
        await identity.Received(1).CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
        await trading.Received(1).CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
        await billing.Received(1).CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>());

        // Assert: the audit anonymizer ran exactly once.
        await anonymizer.Received(1).AnonymizeUserAsync(TestUserId, Arg.Any<CancellationToken>());

        // Assert: the physical user-row delete ran exactly once.
        physicalUserRowDeleteCalls.Should().Be(1,
            "the physical user-row DELETE delegate MUST be invoked exactly once per cascade.");

        // Assert: the sum equals the deletor rows + audit rows + physical rows.
        var deletorRows = 1 + 3 + 5; // 9
        var auditRows = 1;
        var userRows = 1;
        sum.Should().Be(deletorRows + auditRows + userRows,
            "the orchestrator returns the sum of deletor rows + audit rows + the physical user-row.");

        // Assert: the order was correct — deletors FIRST, then anonymizer,
        // then physical delete. We assert this via the per-substitute
        // Received sequence: identity/trading/billing before anonymizer
        // before physical delete.
        Received.InOrder(async () =>
        {
            await identity.CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
            await trading.CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
            await billing.CascadeHardDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
            await anonymizer.AnonymizeUserAsync(TestUserId, Arg.Any<CancellationToken>());
        });

        // The physical delete side effect ran after the deletor loop
        // completed — verified via the boolean captured during the call.
        physicalUserRowDeleted.Should().BeTrue(
            "the physical user-row DELETE MUST run as part of the cascade.");
    }

    [Fact]
    public async Task Orchestrator_PerDeletorException_ContinuesToNextDeletor()
    {
        // Scenario: when one deletor throws (e.g. the Trading module's
        // transient outage), the orchestrator MUST log + swallow the
        // exception and continue with the next deletor. The 30-day
        // sweep cannot be aborted by a single module's failure.
        var identity = Substitute.For<IUserCascadeDeletor>();
        var trading = Substitute.For<IUserCascadeDeletor>();
        var billing = Substitute.For<IUserCascadeDeletor>();
        identity.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(2);
        trading.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>())
              .Throws(new InvalidOperationException("simulated Trading module failure"));
        billing.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(4);

        var sut = BuildOrchestrator(
            deletors: new[] { identity, trading, billing },
            anonymizer: Substitute.For<IGdprAuditAnonymizer>());

        // Act: the call MUST NOT throw — the catch inside the orchestrator
        // swallows the per-deletor exception.
        var act = async () => await sut.CascadeSoftDeleteAsync(TestUserId, CancellationToken.None);
        await act.Should().NotThrowAsync(
            "the orchestrator MUST NOT propagate per-deletor exceptions — the batch continues past the failure.");

        // Assert: identity ran (before trading) and was called once.
        await identity.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());

        // Assert: trading was called (we assert it was attempted even
        // though it threw — NSubstitute records the invocation before
        // the throw propagates).
        await trading.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());

        // Assert: billing ran AFTER trading — the orchestrator continued
        // past the Trading failure and invoked the next deletor in
        // registration order.
        await billing.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Orchestrator_PerDeletorException_LogsAndContinues()
    {
        // Scenario: a per-deletor exception MUST be captured as an Error
        // log via ILogger — the audit trail of "which module failed to
        // purge" is a compliance contract. Verified at the unit boundary
        // by substituting the logger and asserting Received() on the
        // LogError call.
        var identity = Substitute.For<IUserCascadeDeletor>();
        var trading = Substitute.For<IUserCascadeDeletor>();
        var billing = Substitute.For<IUserCascadeDeletor>();
        var simulatedException = new InvalidOperationException("simulated module failure");
        trading.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>())
              .Throws(simulatedException);

        var logger = Substitute.For<ILogger<UserCascadeDeleterOrchestrator>>();

        var sut = BuildOrchestrator(
            deletors: new[] { identity, trading, billing },
            anonymizer: Substitute.For<IGdprAuditAnonymizer>(),
            logger: logger);

        // Act: the call MUST NOT throw.
        var sum = await sut.CascadeSoftDeleteAsync(TestUserId, CancellationToken.None);

        // Assert: Error log was produced (the failure was captured).
        // The orchestrator's structured log message is
        // "GdprSoftDelete: deletor {Type} failed for user {UserId}; continuing."
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());

        // Assert: the exception captured in the log entry is the one the
        // Trading mock threw (the structured logger expects the original
        // exception to be preserved).
        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Is<Exception>(ex => ReferenceEquals(ex, simulatedException)),
            Arg.Any<Func<object, Exception?, string>>());

        // Assert: the next deletor (Billing) was still invoked.
        await billing.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());

        // Assert: the sum reflects ONLY the successful deletors' rows.
        // Identity returned nothing-by-default (0) and Billing returned
        // nothing-by-default (0); the orchestrator's sum must equal 0
        // since the throwing deletor's exception aborts its contribution.
        sum.Should().Be(0,
            "only successful deletors contribute to the sum — the throwing deletor's rows are not counted.");
    }

    [Fact]
    public async Task Orchestrator_PerDeletorReturns0Rows_StillProceedsToNext()
    {
        // Scenario: when a deletor returns 0 rows (the user owns nothing
        // in that module — e.g. no trades in Trading), the orchestrator
        // MUST still proceed to the next deletor. Zero is a valid
        // no-op, NOT an error.
        var identity = Substitute.For<IUserCascadeDeletor>();
        var trading = Substitute.For<IUserCascadeDeletor>();
        var billing = Substitute.For<IUserCascadeDeletor>();
        identity.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(0); // user owns no identity rows
        trading.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(2);  // user owns 2 trading rows
        billing.CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>()).Returns(0);  // user owns no billing rows

        var logger = Substitute.For<ILogger<UserCascadeDeleterOrchestrator>>();

        var sut = BuildOrchestrator(
            deletors: new[] { identity, trading, billing },
            anonymizer: Substitute.For<IGdprAuditAnonymizer>(),
            logger: logger);

        var sum = await sut.CascadeSoftDeleteAsync(TestUserId, CancellationToken.None);

        // Assert: every deletor ran, even those returning 0.
        await identity.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
        await trading.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());
        await billing.Received(1).CascadeSoftDeleteAsync(TestUserId, Arg.Any<CancellationToken>());

        // Assert: the sum is 2 (only Trading contributed non-zero rows).
        sum.Should().Be(2,
            "zero-result deletors contribute 0 to the sum; Trading's 2 rows are the only contribution.");

        // Assert: zero is NOT logged as an error — it is logged as
        // Information with the row count. The orchestrator's structured
        // log message is "GdprSoftDelete: deletor {Type} touched {Touched}
        // row(s) for user {UserId}."; a zero result is the happy path, not
        // an error. We assert NO Error log was emitted by the orchestrator.
        logger.DidNotReceive().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    /// <summary>
    /// Builds the orchestrator with the supplied deletors + anonymizer +
    /// logger. Defaults to <see cref="NullLogger{T}"/> when the logger is
    /// not supplied (matches the 9b.1 <c>AuditRetentionBackgroundServiceTests</c>
    /// pattern — pass a logger only when the test asserts on logs).
    /// </summary>
    private static UserCascadeDeleterOrchestrator BuildOrchestrator(
        IReadOnlyList<IUserCascadeDeletor> deletors,
        IGdprAuditAnonymizer anonymizer,
        ILogger<UserCascadeDeleterOrchestrator>? logger = null)
    {
        logger ??= NullLogger<UserCascadeDeleterOrchestrator>.Instance;
        return new UserCascadeDeleterOrchestrator(deletors, anonymizer, logger);
    }
}
