using FluentAssertions;
using JadeCapital.Admin.Application.Abstractions;
using JadeCapital.Admin.Application.Features.Audit;
using JadeCapital.Shared.Kernel.Audit;
using NSubstitute;

namespace JadeCapital.Admin.UnitTests.Application;

/// <summary>
/// Unit tests for <see cref="ListAuditEventsHandler"/> (Wave 9, slice 9b.1).
///
/// <para>
/// One RED scenario pinned here (per tasks.md §9b.1 Phase 2.1):
/// <b>Handle_RoundTripsCursor_AndClampsLimit</b> — verifies three
/// independent behaviors in one test:
/// <list type="number">
///   <item>DTO mapping: the handler returns the <see cref="PagedAuditEventsDto"/>
///         produced by the <see cref="IAuditEventQueryStore"/> unchanged
///         (the handler is a thin wrapper for the cursor/limit validation).</item>
///   <item>Cursor encode/decode round-trip: a cursor produced by
///         <see cref="ListAuditEventsHandler.EncodeCursor"/> can be
///         decoded by <see cref="ListAuditEventsHandler.TryDecodeCursor"/>
///         back to the same <c>(OccurredAt, Id)</c> pair (the wire
///         contract that the endpoint + tests rely on).</item>
///   <item>Limit clamping: the handler REJECTS out-of-range limits
///         (0 and 201) with <c>validation.audit.limit_out_of_range</c>;
///         it does NOT clamp them silently (per spec).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Why mocking the store</b>: the handler is a thin wrapper around
/// <see cref="IAuditEventQueryStore"/>. The store's EF query behavior
/// is covered by <c>AuditEventQueryStoreTests</c> (RED SQLite
/// integration tests). Here we test ONLY the handler's cursor/limit
/// validation logic, which is the unit-testable surface.
/// </para>
/// </summary>
public class ListAuditEventsHandlerTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    private static PagedAuditEventsDto BuildPage(int count) =>
        new(
            Items: Enumerable.Range(0, count).Select(_ => new AuditEventDto(
                Id: Guid.NewGuid(),
                EntityType: "TradeAttachment",
                EntityId: Guid.NewGuid(),
                Action: AuditAction.Updated,
                TenantId: Guid.NewGuid(),
                UserId: Guid.NewGuid(),
                Changes: null,
                OccurredAt: FixedNow)).ToList(),
            NextCursor: null,
            HasMore: false);

    [Fact]
    public async Task Handle_RoundTripsCursor_AndClampsLimit()
    {
        // Phase 2.1 #1: the handler round-trips the store's DTO
        // unchanged, validates the cursor shape, and rejects out-of-range
        // limits. We exercise the three sub-cases inline:
        //   (a) Round-trip: store returns a DTO → handler returns
        //       the SAME DTO (no reshape, no transformation).
        //   (b) Cursor: encode + decode preserves (OccurredAt, Id).
        //   (c) Limit clamping: 0 and 201 are rejected with the
        //       validation code; 1 and 200 are accepted.

        var store = Substitute.For<IAuditEventQueryStore>();
        var expectedPage = BuildPage(2);
        store.ListAsync(Arg.Any<ListAuditEventsQuery>(), Arg.Any<CancellationToken>())
             .Returns(expectedPage);

        var sut = new ListAuditEventsHandler(store);

        // (a) DTO round-trip
        var okReq = new ListAuditEventsQuery(
            EntityType: null, Action: null, UserId: null, TenantId: null,
            From: null, To: null, Cursor: null, Limit: 50);
        var result = await sut.Handle(okReq, CancellationToken.None);
        result.IsSuccess.Should().BeTrue("a valid request returns the store's DTO unchanged.");
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items[0].EntityType.Should().Be("TradeAttachment");

        // (b) Cursor encode/decode round-trip
        var id = Guid.NewGuid();
        var cursor = ListAuditEventsHandler.EncodeCursor(FixedNow, id);
        var ok = ListAuditEventsHandler.TryDecodeCursor(cursor, out var roundTrip);
        ok.Should().BeTrue("cursor encode/decode must succeed for a cursor produced by EncodeCursor.");
        roundTrip.OccurredAt.Should().Be(FixedNow,
            "the cursor round-trip preserves the original OccurredAt timestamp.");
        roundTrip.Id.Should().Be(id,
            "the cursor round-trip preserves the original Id.");

        // (c) Limit clamping — REJECT (not silent clamp)
        var tooSmall = await sut.Handle(okReq with { Limit = 0 }, CancellationToken.None);
        tooSmall.IsFailure.Should().BeTrue("limit=0 is below the [1, 200] window.");
        tooSmall.Error.Code.Should().Be("validation.audit.limit_out_of_range",
            "out-of-range limits return the validation error code per spec.");

        var tooLarge = await sut.Handle(okReq with { Limit = 201 }, CancellationToken.None);
        tooLarge.IsFailure.Should().BeTrue("limit=201 is above the [1, 200] window.");
        tooLarge.Error.Code.Should().Be("validation.audit.limit_out_of_range");

        // Boundary: 1 and 200 are accepted (clamping is INCLUSIVE).
        var minBound = await sut.Handle(okReq with { Limit = 1 }, CancellationToken.None);
        minBound.IsSuccess.Should().BeTrue("limit=1 is the inclusive lower bound.");
        var maxBound = await sut.Handle(okReq with { Limit = 200 }, CancellationToken.None);
        maxBound.IsSuccess.Should().BeTrue("limit=200 is the inclusive upper bound.");

        // (d) Malformed cursor is rejected BEFORE the store call.
        var badCursor = await sut.Handle(
            okReq with { Cursor = "not-base64-!!!" }, CancellationToken.None);
        badCursor.IsFailure.Should().BeTrue("a malformed cursor is rejected with the validation error code.");
        badCursor.Error.Code.Should().Be("validation.audit.invalid_cursor");
        await store.DidNotReceive().ListAsync(
            Arg.Is<ListAuditEventsQuery>(q => q.Cursor == "not-base64-!!!"),
            Arg.Any<CancellationToken>());
    }
}
