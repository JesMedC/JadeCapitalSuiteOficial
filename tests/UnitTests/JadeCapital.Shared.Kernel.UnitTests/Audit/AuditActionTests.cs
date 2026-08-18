using FluentAssertions;
using JadeCapital.Shared.Kernel.Audit;

namespace JadeCapital.Shared.Kernel.UnitTests.Audit;

/// <summary>
/// Wave 7 slice 7a.1 — <see cref="AuditAction"/> enum extension.
/// The Wave 6 6d.1 enum had Created=0, Updated=1, Deleted=2, Restored=3.
/// Wave 7 adds <see cref="AuditAction.Denied"/>=4 (cross-tenant access
/// attempt) and <see cref="AuditAction.Failed"/>=5 (DeleteAsync
/// <c>NotSupportedException</c> path). The byte values map 1:1 to
/// <c>audit.events.action SMALLINT</c>; the DB CHECK constraint
/// <c>ck_audit_events_action</c> is widened to 0..5 in migration 0029
/// atomically with the enum extension.
///
/// Three RED scenarios pinned here:
/// <list type="number">
///   <item><see cref="AuditAction.Denied"/> exists and serializes to byte 4.</item>
///   <item><see cref="AuditAction.Failed"/> exists and serializes to byte 5.</item>
///   <item><see cref="AuditAction.Created"/> + <see cref="AuditAction.Updated"/> +
///         <see cref="AuditAction.Deleted"/> + <see cref="AuditAction.Restored"/>
///         remain at bytes 0/1/2/3 — the historical <c>audit.events.action</c>
///         rows MUST remain readable.</item>
/// </list>
/// </summary>
public class AuditActionTests
{
    [Fact]
    public void Denied_HasByteValueFour()
    {
        // Phase 1 #1: AuditAction.Denied = 4.
        ((byte)AuditAction.Denied).Should().Be(4);
        AuditAction.Denied.ToString().Should().Be(nameof(AuditAction.Denied));
    }

    [Fact]
    public void Failed_HasByteValueFive()
    {
        // Phase 1 #2: AuditAction.Failed = 5.
        ((byte)AuditAction.Failed).Should().Be(5);
        AuditAction.Failed.ToString().Should().Be(nameof(AuditAction.Failed));
    }

    [Fact]
    public void ExistingWave6Values_AreUnchanged_AtBytesZeroThroughThree()
    {
        // Phase 1 #3: no renumbering — historical audit.events rows remain readable.
        ((byte)AuditAction.Created).Should().Be(0);
        ((byte)AuditAction.Updated).Should().Be(1);
        ((byte)AuditAction.Deleted).Should().Be(2);
        ((byte)AuditAction.Restored).Should().Be(3);
    }
}