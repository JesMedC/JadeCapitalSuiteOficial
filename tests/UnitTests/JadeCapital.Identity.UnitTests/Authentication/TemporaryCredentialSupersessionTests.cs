using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.UnitTests.Authentication;

/// <summary>
/// Domain tests for the atomic supersession transition on TemporaryCredential
/// (slice 0c supersession addendum of jade-trader-os-core-portals).
///
/// The transition Activated -> Superseded is the "atomic supersession" step
/// executed inside the ForgotPasswordHandler BEFORE a new Pending row is
/// reserved. A concurrent reset must leave exactly one Activated row per user;
/// the unique partial index `ux_temporary_credentials_user_active` enforces
/// the persistence-level guarantee, while the domain transition is the
/// application-level guarantee the handler relies on.
///
/// Idempotency: calling MarkSuperseded on a row that is already Superseded
/// (or Consumed) is a no-op success — the sweeper and out-of-order calls
/// from the orchestrator must be safe to retry without surfacing spurious
/// errors.
/// </summary>
public class TemporaryCredentialSupersessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateTimeOffset Later = Now.AddHours(1);

    private static TemporaryCredential CreateActivated(int generation = 1, Guid? userId = null)
    {
        var c = TemporaryCredential.Reserve(
            Guid.NewGuid(),
            userId ?? Guid.NewGuid(),
            generation,
            CredentialHash.From("hashed"),
            Now).Value;
        c.Activate(Later, latestGeneration: generation);
        return c;
    }

    [Fact]
    public void MarkSuperseded_FromActivated_TransitionsToSuperseded()
    {
        var c = CreateActivated();

        var r = c.MarkSuperseded(Later.AddMinutes(30));

        r.IsSuccess.Should().BeTrue();
        c.Status.Should().Be(TemporaryCredentialStatus.Superseded);
        c.IsUsable(Later.AddMinutes(30)).Should().BeFalse("a superseded row must never authenticate");
    }

    [Fact]
    public void MarkSuperseded_AlreadySuperseded_IsIdempotent()
    {
        var c = CreateActivated();
        c.MarkSuperseded(Later.AddMinutes(1));

        var r = c.MarkSuperseded(Later.AddMinutes(2));

        r.IsSuccess.Should().BeTrue("repeated supersession must not surface as a failure");
        c.Status.Should().Be(TemporaryCredentialStatus.Superseded);
    }

    [Fact]
    public void MarkSuperseded_FromConsumed_IsIdempotent()
    {
        var c = CreateActivated();
        c.MarkConsumed(Later.AddMinutes(1), "grant-jti-1");

        var r = c.MarkSuperseded(Later.AddMinutes(2));

        // Consumed is terminal from a credential standpoint; the orchestrator's
        // sweeper must be able to safely no-op on rows it cannot transition.
        r.IsSuccess.Should().BeTrue();
        c.Status.Should().Be(TemporaryCredentialStatus.Consumed,
            "MarkSuperseded must not erase the Consumed state — single-use audit trail wins");
    }

    [Fact]
    public void MarkSuperseded_FromPending_Fails()
    {
        var c = TemporaryCredential.Reserve(
            Guid.NewGuid(), Guid.NewGuid(), 1, CredentialHash.From("h"), Now).Value;

        var r = c.MarkSuperseded(Later);

        r.IsFailure.Should().BeTrue();
        c.Status.Should().Be(TemporaryCredentialStatus.Pending,
            "only an already-Activated row can be superseded — Pending rows are intentionally transitional");
    }
}