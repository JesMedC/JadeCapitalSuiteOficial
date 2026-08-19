using System.Globalization;
using System.Text;
using JadeCapital.Admin.Application.Abstractions;
using JadeCapital.Shared.Kernel.Results;
using MediatR;

namespace JadeCapital.Admin.Application.Features.Audit;

/// <summary>
/// MediatR handler for <see cref="ListAuditEventsQuery"/> (Wave 9,
/// slice 9b.1 — admin query API, sub-scope B).
///
/// <para>
/// Responsibilities:
/// <list type="number">
///   <item>Validate <see cref="ListAuditEventsQuery.Limit"/> against the
///         <c>[1, 200]</c> window — values outside return a
///         <see cref="Result.Failure{T}(Error)"/> with
///         <c>validation.audit.limit_out_of_range</c>.</item>
///   <item>Validate the opaque base64 cursor — malformed cursors return
///         <c>validation.audit.invalid_cursor</c>. No DB lookup happens
///         for an invalid cursor (per spec §Malformed cursor returns
///         400).</item>
///   <item>Dispatch to <see cref="IAuditEventQueryStore.ListAsync"/> and
///         return the <see cref="PagedAuditEventsDto"/> wrapped in
///         <see cref="Result.Success{T}(T)"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// The handler does NOT emit an <c>audit.events</c> row for the admin's
/// own query (no audit-on-audit-query — would be recursive). The
/// AdminActor identity is captured in the Serilog request log by the
/// endpoint (mirrors the spec's PII contract).
/// </para>
///
/// <para>
/// <b>Cursor format</b>: <c>base64("{occurred_at_ticks}:{id_guid}")</c>
/// — opaque to clients; the handler decodes + re-encodes transparently.
/// The base64 is URL-safe (the default <see cref="Convert.ToBase64String"/>
/// output uses the standard alphabet; the test client encodes the same
/// way to produce fixture cursors).
/// </para>
/// </summary>
public sealed class ListAuditEventsHandler
    : IRequestHandler<ListAuditEventsQuery, Result<PagedAuditEventsDto>>
{
    private readonly IAuditEventQueryStore _store;

    public ListAuditEventsHandler(IAuditEventQueryStore store)
    {
        _store = store;
    }

    public async Task<Result<PagedAuditEventsDto>> Handle(
        ListAuditEventsQuery req, CancellationToken ct)
    {
        // 1) Limit clamp — out-of-range is a 400, not a server-side clamp.
        //    The spec mandates explicit rejection so clients learn the
        //    contract instead of silently receiving fewer rows.
        if (req.Limit < ListAuditEventsQuery.MinLimit || req.Limit > ListAuditEventsQuery.MaxLimit)
        {
            return Result.Failure<PagedAuditEventsDto>(Error.Validation(
                "audit.limit_out_of_range",
                $"Limit must be between {ListAuditEventsQuery.MinLimit} and {ListAuditEventsQuery.MaxLimit}."));
        }

        // 2) Cursor validation — decode BEFORE the store call so a
        //    malformed cursor never reaches EF (no SQL injection surface,
        //    no wasted DB roundtrip).
        if (!string.IsNullOrEmpty(req.Cursor) && !TryDecodeCursor(req.Cursor, out _))
        {
            return Result.Failure<PagedAuditEventsDto>(Error.Validation(
                "audit.invalid_cursor",
                "Cursor is malformed; expected base64('{occurred_at_ticks}:{guid}')."));
        }

        var page = await _store.ListAsync(req, ct);
        return Result.Success(page);
    }

    /// <summary>
    /// Decodes the opaque cursor — public so the endpoint (and tests)
    /// can validate the cursor before dispatch. Returns
    /// <c>false</c> on any base64 / shape failure.
    /// </summary>
    public static bool TryDecodeCursor(string cursor, out (DateTimeOffset OccurredAt, Guid Id) value)
    {
        value = default;
        try
        {
            var bytes = Convert.FromBase64String(cursor);
            var text = Encoding.UTF8.GetString(bytes);
            var parts = text.Split('|');
            if (parts.Length != 2) return false;

            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
                return false;
            if (!Guid.TryParse(parts[1], out var id))
                return false;

            value = (new DateTimeOffset(ticks, TimeSpan.Zero), id);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Encodes the next-page cursor from the last item's
    /// <c>(occurred_at, id)</c>. Public so the test fixture can produce
    /// expected <c>next_cursor</c> values deterministically.
    /// </summary>
    public static string EncodeCursor(DateTimeOffset occurredAt, Guid id)
    {
        var text = $"{occurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id:D}";
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(bytes);
    }
}