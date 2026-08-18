using System.Reflection;
using FluentAssertions;
using JadeCapital.Trading.Application.Abstractions;
using JadeCapital.Trading.Domain.Journal;

namespace JadeCapital.Identity.UnitTests.Abstractions;

/// <summary>
/// Wave 7 slice 7b.2 — <see cref="IJournalEntryRepository"/> additive overload surgery.
///
/// <para>
/// Slice 7b.2 ADDS a <c>DeleteAsync(JournalEntry, CancellationToken)</c>
/// overload to the existing bespoke interface (which currently only
/// exposes <c>DeleteAsync(Guid, CancellationToken)</c>). The new overload
/// is the path the <c>JournalEntryAuditDecorator</c> wraps — it
/// internally delegates to the existing Guid overload.
/// </para>
///
/// <para>
/// <b>Why an additive overload and not a rename</b>: the production
/// handler <c>DeleteJournalEntryHandler</c> (and only that handler)
/// uses the Guid-only path. The slice is purely additive — no existing
/// call site changes, no breaking rename, no [Obsolete] attribute.
/// Production handlers can use either signature after the slice lands.
/// </para>
///
/// <para>
/// <b>Why not extend <c>IRepository&lt;JournalEntry&gt;</c></b>: the
/// interface is bespoke (mirrors the 7b.1 <c>ITradeRepository</c> shape)
/// with cross-user scope methods (GetByUserAndDateAsync,
/// ListByRangeAsync, FindByIdAsync) that take an explicit
/// <c>userId</c> parameter. Extending <c>IRepository&lt;T&gt;</c> would
/// force a <c>GetByIdAsync(Guid, ct)</c> signature that ignores userId —
/// a security regression (the canonical lookup is the userId-scoped
/// <c>FindByIdAsync(entryId, userId, ct)</c>). The bespoke shape is
/// the correct design.
/// </para>
///
/// Two RED scenarios pinned here (per tasks.md §7b.2 Phase 1):
/// <list type="number">
///   <item><c>IJournalEntryRepository.DeleteAsync(JournalEntry, CancellationToken)</c>
///         method exists on the interface (slice 7b.2 additive overload).</item>
///   <item><c>IJournalEntryRepository.DeleteAsync(Guid, CancellationToken)</c>
///         is the production path AND remains on the interface (regression
///         guard — slice 7b.2 must not touch the existing Guid overload).</item>
/// </list>
/// </summary>
public class IJournalEntryRepositoryContractTests
{
    [Fact]
    public void IJournalEntryRepository_Exposes_DeleteAsyncByEntityOverload()
    {
        // Phase 1 #1: DeleteAsync(JournalEntry, CancellationToken) exists on
        // the interface (slice 7b.2 additive overload). The overload is the
        // path the JournalEntryAuditDecorator wraps. Production handlers can
        // also call it directly when they have the entity in scope.
        var method = typeof(IJournalEntryRepository).GetMethod(
            "DeleteAsync",
            new[] { typeof(JournalEntry), typeof(CancellationToken) });

        method.Should().NotBeNull(
            "IJournalEntryRepository must expose " +
            "DeleteAsync(JournalEntry, CancellationToken) — the slice 7b.2 " +
            "additive overload. The JournalEntryAuditDecorator wraps this " +
            "overload; production handlers can also call it directly.");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync is a Task-returning async method (no result payload).");
    }

    [Fact]
    public void IJournalEntryRepository_Retains_DeleteAsyncByGuidOverload()
    {
        // Phase 1 #2: DeleteAsync(Guid, CancellationToken) is the existing
        // production path AND remains on the interface. Regression guard —
        // slice 7b.2 must NOT remove the Guid overload; it's used by
        // DeleteJournalEntryHandler and is the canonical cross-user-safe
        // delete (no entity-arg required, so the handler doesn't need to
        // pre-load the entity just to call delete).
        var method = typeof(IJournalEntryRepository).GetMethod(
            "DeleteAsync",
            new[] { typeof(Guid), typeof(CancellationToken) });

        method.Should().NotBeNull(
            "IJournalEntryRepository must retain DeleteAsync(Guid, CancellationToken) " +
            "— the existing production path used by DeleteJournalEntryHandler. " +
            "Slice 7b.2 is additive; the Guid overload MUST stay.");
        method!.ReturnType.Should().Be<Task>(
            "DeleteAsync(Guid, ct) is a Task-returning async method (no result payload).");
    }
}
