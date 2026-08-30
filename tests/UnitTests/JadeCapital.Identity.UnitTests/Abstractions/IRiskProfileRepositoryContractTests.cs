using System.Reflection;
using FluentAssertions;
using JadeCapital.Identity.Application.Abstractions;
using JadeCapital.Identity.Domain.RiskProfile;
using JadeCapital.Identity.Infrastructure.Persistence;
using JadeCapital.Shared.Kernel.Results;
using JadeCapital.Shared.Kernel.Time;

namespace JadeCapital.Identity.UnitTests.Abstractions;

/// <summary>
/// Wave 7 slice 7a.1 — <see cref="IRiskProfileRepository"/> contract surgery.
///
/// <para>
/// Slice 7a.1 adds a <c>DeleteAsync(RiskProfile, CancellationToken)</c>
/// method to the interface. The contract is:
/// </para>
/// <list type="bullet">
///   <item>The canonical RiskProfile supersede surface is
///   <c>MarkSupersededAsync(Guid profileId, IClock clock, CancellationToken ct)</c>
///   (already present from slice 1a.1b / 4b). MarkSuperseded flips
///   <c>IsActive = false</c> + sets <c>SupersededAt = now</c> via the
///   aggregate's <c>RiskProfile.MarkSuperseded(IClock)</c> domain op.</item>
///   <item>The decorator (<c>RiskProfileAuditDecorator</c>) wraps
///   <c>MarkSupersededAsync</c> DIRECTLY (NOT <c>UpdateAsync</c> — there is
///   no <c>UpdateAsync</c> on the canonical mutation surface; the supersede
///   IS the termination). On supersede the decorator emits
///   <see cref="Audit.AuditAction.Deleted"/> + the supersession diff.</item>
///   <item>The decorator short-circuits <c>DeleteAsync</c> to an
///   <see cref="Audit.AuditAction.Failed"/> audit row + re-throws
///   <see cref="NotSupportedException"/> BEFORE the inner is reached.</item>
///   <item>The inner (<c>RiskProfileRepository.DeleteAsync(RiskProfile, ct)</c>)
///   is a defensive STUB that throws with the canonical message
///   <c>"RiskProfile deletion happens via MarkSupersededAsync, not direct delete"</c>
///   so the failure mode is identical whether the caller accidentally bypasses
///   the decorator OR the decorator is misconfigured.</item>
/// </list>
///
/// Three RED scenarios pinned here:
/// <list type="number">
///   <item><c>IRiskProfileRepository.MarkSupersededAsync(Guid, IClock, CancellationToken)</c>
///         exists with the expected signature (Wave 1a.1b baseline — pinned
///         here so the slice cannot silently regress it).</item>
///   <item><c>IRiskProfileRepository.DeleteAsync(RiskProfile, CancellationToken)</c>
///         method exists on the interface (slice 7a.1 addition).</item>
///   <item>The concrete <c>RiskProfileRepository.DeleteAsync(RiskProfile, ct)</c>
///         STUB throws <see cref="NotSupportedException"/> with the canonical
///         message that mentions <c>"MarkSupersededAsync"</c>.</item>
/// </list>
/// </summary>
public class IRiskProfileRepositoryContractTests
{
    [Fact]
    public void IRiskProfileRepository_Exposes_MarkSupersededAsync()
    {
        // Phase 3 #1: MarkSupersededAsync(Guid, IClock, CancellationToken)
        // baseline — pinned from Wave 1a.1b so the 7a.1 surgery does not
        // silently drop or rename it.
        var method = typeof(IRiskProfileRepository).GetMethod(
            "MarkSupersededAsync",
            new[] { typeof(Guid), typeof(IClock), typeof(CancellationToken) });

        method.Should().NotBeNull(
            "IRiskProfileRepository.MarkSupersededAsync(Guid, IClock, CancellationToken) " +
            "is the canonical supersede surface from slice 1a.1b.");
        method!.ReturnType.Should().Be<Task<Result>>(
            "MarkSupersededAsync returns Task<Result> so the handler can map " +
            "race-condition failures to 409 conflict per slice 4b.");
    }

    [Fact]
    public void IRiskProfileRepository_Exposes_DeleteAsyncMethod()
    {
        // Phase 3 #2: DeleteAsync(RiskProfile, CancellationToken) is the
        // new slice 7a.1 addition. Reflection verifies the surface surgery.
        var method = typeof(IRiskProfileRepository).GetMethod(
            "DeleteAsync",
            new[] { typeof(RiskProfile), typeof(CancellationToken) });

        method.Should().NotBeNull(
            "IRiskProfileRepository must expose DeleteAsync(RiskProfile, CancellationToken) " +
            "as part of the Slice 7a.1 surface surgery (RiskProfileAuditDecorator wraps it).");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync is a Task-returning async method (no result payload).");
    }

    [Fact]
    public async Task RiskProfileRepository_DeleteAsync_ThrowsNotSupportedException_WithCanonicalMessage()
    {
        // Phase 3 #3: the concrete RiskProfileRepository stub throws with a
        // message containing "MarkSupersededAsync" so the
        // RiskProfileAuditDecorator can capture the message verbatim in the
        // audit row's ChangesJson. The decorator short-circuits BEFORE this
        // stub is reached, but the inner must also throw — defense-in-depth.
        //
        // DeleteAsync is a stub that throws immediately; the DbContext is
        // never touched. Passing a null IdentityDbContext is safe.
        var repo = new RiskProfileRepository(db: null!);

        var profile = RiskProfile.Create(
            id: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            capital: JadeCapital.Shared.Kernel.Money.Money.FromTrusted(10_000m, "USD"),
            maxDrawdown: JadeCapital.Identity.Domain.RiskProfile.MaxDrawdownPercent.Create(5m).Value,
            riskPerTrade: JadeCapital.Identity.Domain.RiskProfile.RiskPerTradePercent.Create(0.5m).Value,
            rrTarget: JadeCapital.Identity.Domain.RiskProfile.RiskRewardRatio.Create(2m).Value,
            clock: new FixedClockForTest()).Value;

        var act = async () => await repo.DeleteAsync(profile, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*MarkSupersededAsync*");
    }

    private sealed class FixedClockForTest : IClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);
    }
}